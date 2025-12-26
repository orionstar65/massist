import { NextRequest, NextResponse } from 'next/server';
import { requireAuth } from '@/lib/auth';
import { query } from '@/lib/db';
import { Prompt } from '@/lib/types';
import { z } from 'zod';

const clonePromptSchema = z.object({
  name: z.string().min(1).max(255),
});

// POST /api/prompts/:id/clone - Clone a prompt
export async function POST(
  request: NextRequest,
  { params }: { params: { id: string } }
) {
  const authResult = await requireAuth(request);

  if (!authResult) {
    return NextResponse.json(
      { error: 'Unauthorized' },
      { status: 401 }
    );
  }

  try {
    const promptId = parseInt(params.id);
    const body = await request.json();
    const { name } = clonePromptSchema.parse(body);

    // Get the original prompt (can be from any user for cloning)
    const originalPrompts = await query<Prompt[]>(
      'SELECT * FROM prompts WHERE id = ?',
      [promptId]
    );

    if (originalPrompts.length === 0) {
      return NextResponse.json(
        { error: 'Prompt not found' },
        { status: 404 }
      );
    }

    const originalPrompt = originalPrompts[0];

    // Create new prompt with same content but new name (owned by current user)
    const result = await query<any>(
      'INSERT INTO prompts (name, prompt, description, is_default, created_by) VALUES (?, ?, ?, ?, ?)',
      [name, originalPrompt.prompt, originalPrompt.description || null, false, authResult.user.id]
    );

    const newPromptId = (result as any).insertId;

    // Associate cloned prompt with current user
    await query(
      'INSERT INTO user_prompts (user_id, prompt_id) VALUES (?, ?)',
      [authResult.user.id, newPromptId]
    );

    // Fetch cloned prompt
    const clonedPrompts = await query<Prompt[]>(
      'SELECT * FROM prompts WHERE id = ?',
      [newPromptId]
    );

    return NextResponse.json(clonedPrompts[0], { status: 201 });
  } catch (error) {
    if (error instanceof z.ZodError) {
      return NextResponse.json(
        { error: 'Invalid input', details: error.errors },
        { status: 400 }
      );
    }

    console.error('Error cloning prompt:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

