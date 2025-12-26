import { NextResponse } from 'next/server';

export async function POST() {
  // Since we're using JWT tokens, logout is handled client-side
  // by removing the token. This endpoint is for consistency.
  return NextResponse.json({ success: true });
}

