import { NextRequest, NextResponse } from 'next/server';
import { requireAuth } from '@/lib/auth';
import { query } from '@/lib/db';
import { Prompt } from '@/lib/types';
import { z } from 'zod';

const updatePromptSchema = z.object({
  name: z.string().min(1).max(255).optional(),
  prompt: z.string().min(1).optional(),
  description: z.string().optional(),
  is_default: z.boolean().optional(),
});

// GET /api/prompts/:id - Get a specific prompt
export async function GET(
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

    // Get prompt with creator info and user's adoption status
    const prompts = await query<any[]>(
      `SELECT 
        p.*,
        u.username as created_by_username,
        CASE WHEN p.created_by = ? THEN 1 ELSE 0 END as is_owned,
        CASE WHEN up.user_id IS NOT NULL THEN 1 ELSE 0 END as is_adopted
       FROM prompts p
       LEFT JOIN users u ON p.created_by = u.id
       LEFT JOIN user_prompts up ON p.id = up.prompt_id AND up.user_id = ?
       WHERE p.id = ?`,
      [authResult.user.id, authResult.user.id, promptId]
    );

    if (prompts.length === 0) {
      return NextResponse.json(
        { error: 'Prompt not found' },
        { status: 404 }
      );
    }

    return NextResponse.json(prompts[0]);
  } catch (error) {
    console.error('Error fetching prompt:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

// PUT /api/prompts/:id - Update a prompt
export async function PUT(
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

    // Check if prompt exists and if user owns it
    const prompts = await query<any[]>(
      'SELECT created_by FROM prompts WHERE id = ?',
      [promptId]
    );

    if (prompts.length === 0) {
      return NextResponse.json(
        { error: 'Prompt not found' },
        { status: 404 }
      );
    }

    // Only allow editing if user owns the prompt
    if (prompts[0].created_by !== authResult.user.id) {
      return NextResponse.json(
        { error: 'You can only edit prompts you created' },
        { status: 403 }
      );
    }

    const body = await request.json();
    const updateData = updatePromptSchema.parse(body);

    // Build update query dynamically
    const updateFields: string[] = [];
    const updateValues: any[] = [];

    if (updateData.name !== undefined) {
      updateFields.push('name = ?');
      updateValues.push(updateData.name);
    }
    if (updateData.prompt !== undefined) {
      updateFields.push('prompt = ?');
      updateValues.push(updateData.prompt);
    }
    if (updateData.description !== undefined) {
      updateFields.push('description = ?');
      updateValues.push(updateData.description);
    }
    if (updateData.is_default !== undefined) {
      updateFields.push('is_default = ?');
      updateValues.push(updateData.is_default);
    }

    if (updateFields.length === 0) {
      return NextResponse.json(
        { error: 'No fields to update' },
        { status: 400 }
      );
    }

    updateValues.push(promptId);

    await query(
      `UPDATE prompts SET ${updateFields.join(', ')} WHERE id = ?`,
      updateValues
    );

    // Fetch updated prompt with full info
    const updatedPrompts = await query<any[]>(
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

    return NextResponse.json(updatedPrompts[0]);
  } catch (error) {
    if (error instanceof z.ZodError) {
      return NextResponse.json(
        { error: 'Invalid input', details: error.errors },
        { status: 400 }
      );
    }

    console.error('Error updating prompt:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

// DELETE /api/prompts/:id - Delete a prompt
export async function DELETE(
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

    // Check if prompt exists and if user owns it
    const prompts = await query<any[]>(
      'SELECT created_by FROM prompts WHERE id = ?',
      [promptId]
    );

    if (prompts.length === 0) {
      return NextResponse.json(
        { error: 'Prompt not found' },
        { status: 404 }
      );
    }

    // Only allow deleting if user owns the prompt
    if (prompts[0].created_by !== authResult.user.id) {
      return NextResponse.json(
        { error: 'You can only delete prompts you created' },
        { status: 403 }
      );
    }

    // Delete prompt (cascade will handle user_prompts)
    await query('DELETE FROM prompts WHERE id = ?', [promptId]);

    return NextResponse.json({ success: true });
  } catch (error) {
    console.error('Error deleting prompt:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

