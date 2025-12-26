import { NextRequest, NextResponse } from 'next/server';
import { requireAuth } from '@/lib/auth';

export async function GET(request: NextRequest) {
  const authResult = await requireAuth(request);

  if (!authResult) {
    return NextResponse.json(
      { error: 'Unauthorized' },
      { status: 401 }
    );
  }

  return NextResponse.json({
    user: {
      id: authResult.user.id,
      username: authResult.user.username,
    },
  });
}

