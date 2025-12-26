import { NextRequest, NextResponse } from 'next/server';
import { requireAuth } from '@/lib/auth';
import { query } from '@/lib/db';
import { Prompt } from '@/lib/types';
import { z } from 'zod';

const createPromptSchema = z.object({
  name: z.string().min(1).max(255),
  prompt: z.string().min(1),
  description: z.string().optional(),
  is_default: z.boolean().optional(),
});

// GET /api/prompts - Get all prompts (with user's ownership/adoption status)
export async function GET(request: NextRequest) {
  const authResult = await requireAuth(request);

  if (!authResult) {
    return NextResponse.json(
      { error: 'Unauthorized' },
      { status: 401 }
    );
  }

  try {
    // Get all prompts with creator info and user's adoption status
    const prompts = await query<any[]>(
      `SELECT 
        p.id, 
        p.name, 
        p.prompt, 
        p.description, 
        p.is_default, 
        p.created_by,
        u.username as created_by_username,
        CASE WHEN p.created_by = ? THEN 1 ELSE 0 END as is_owned,
        CASE WHEN up.user_id IS NOT NULL THEN 1 ELSE 0 END as is_adopted,
        p.created_at, 
        p.updated_at
       FROM prompts p
       LEFT JOIN users u ON p.created_by = u.id
       LEFT JOIN user_prompts up ON p.id = up.prompt_id AND up.user_id = ?
       ORDER BY p.created_at DESC`,
      [authResult.user.id, authResult.user.id]
    );

    return NextResponse.json({ prompts });
  } catch (error) {
    console.error('Error fetching prompts:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

// POST /api/prompts - Create a new prompt for authenticated user
export async function POST(request: NextRequest) {
  const authResult = await requireAuth(request);

  if (!authResult) {
    return NextResponse.json(
      { error: 'Unauthorized' },
      { status: 401 }
    );
  }

  try {
    const body = await request.json();
    const { name, prompt, description, is_default } = createPromptSchema.parse(body);

    // Create prompt with created_by
    const result = await query<any>(
      'INSERT INTO prompts (name, prompt, description, is_default, created_by) VALUES (?, ?, ?, ?, ?)',
      [name, prompt, description || null, is_default || false, authResult.user.id]
    );

    const promptId = (result as any).insertId;

    // Associate prompt with creator (so they own it)
    await query(
      'INSERT INTO user_prompts (user_id, prompt_id) VALUES (?, ?)',
      [authResult.user.id, promptId]
    );

    // Fetch created prompt with full info
    const createdPrompts = await query<any[]>(
      `SELECT 
        p.*,
        u.username as created_by_username,
        1 as is_owned,
        1 as is_adopted
       FROM prompts p
       LEFT JOIN users u ON p.created_by = u.id
       WHERE p.id = ?`,
      [promptId]
    );

    return NextResponse.json(createdPrompts[0], { status: 201 });
  } catch (error) {
    if (error instanceof z.ZodError) {
      return NextResponse.json(
        { error: 'Invalid input', details: error.errors },
        { status: 400 }
      );
    }

    console.error('Error creating prompt:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

