import { NextRequest, NextResponse } from 'next/server';
import { requireAuth } from '@/lib/auth';
import { query } from '@/lib/db';

// POST /api/prompts/:id/adopt - Adopt a prompt (add to user's collection)
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

    // Check if prompt exists
    const prompts = await query<any[]>(
      'SELECT id FROM prompts WHERE id = ?',
      [promptId]
    );

    if (prompts.length === 0) {
      return NextResponse.json(
        { error: 'Prompt not found' },
        { status: 404 }
      );
    }

    // Check if user already has this prompt
    const existing = await query<any[]>(
      'SELECT id FROM user_prompts WHERE user_id = ? AND prompt_id = ?',
      [authResult.user.id, promptId]
    );

    if (existing.length > 0) {
      return NextResponse.json(
        { error: 'Prompt already in your collection' },
        { status: 400 }
      );
    }

    // Add prompt to user's collection
    await query(
      'INSERT INTO user_prompts (user_id, prompt_id) VALUES (?, ?)',
      [authResult.user.id, promptId]
    );

    return NextResponse.json({ success: true, message: 'Prompt adopted successfully' });
  } catch (error) {
    console.error('Error adopting prompt:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

// DELETE /api/prompts/:id/adopt - Remove a prompt from user's collection (only if not owned)
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

    // Check if user owns this prompt
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

    // Don't allow removing if user owns it (they should delete it instead)
    if (prompts[0].created_by === authResult.user.id) {
      return NextResponse.json(
        { error: 'Cannot remove owned prompt. Use delete instead.' },
        { status: 400 }
      );
    }

    // Remove from user's collection
    await query(
      'DELETE FROM user_prompts WHERE user_id = ? AND prompt_id = ?',
      [authResult.user.id, promptId]
    );

    return NextResponse.json({ success: true, message: 'Prompt removed from collection' });
  } catch (error) {
    console.error('Error removing prompt:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

