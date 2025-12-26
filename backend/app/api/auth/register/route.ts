import { NextRequest, NextResponse } from 'next/server';
import bcrypt from 'bcryptjs';
import { query } from '@/lib/db';
import { generateToken } from '@/lib/auth';
import { z } from 'zod';

const registerSchema = z.object({
  username: z.string().min(3).max(255),
  password: z.string().min(6).max(255),
});

export async function POST(request: NextRequest) {
  try {
    const body = await request.json();
    const { username, password } = registerSchema.parse(body);

    // Check if user already exists
    const existingUsers = await query<any[]>(
      'SELECT id FROM users WHERE username = ?',
      [username]
    );

    if (existingUsers.length > 0) {
      return NextResponse.json(
        { error: 'Username already exists' },
        { status: 400 }
      );
    }

    // Hash password
    const password_hash = await bcrypt.hash(password, 10);

    // Create user
    const result = await query<any>(
      'INSERT INTO users (username, password_hash) VALUES (?, ?)',
      [username, password_hash]
    );

    const userId = (result as any).insertId;

    // Generate token
    const token = generateToken({ id: userId, username });

    return NextResponse.json({
      user: {
        id: userId,
        username,
      },
      token,
    });
  } catch (error) {
    if (error instanceof z.ZodError) {
      return NextResponse.json(
        { error: 'Invalid input', details: error.errors },
        { status: 400 }
      );
    }

    console.error('Registration error:', error);
    return NextResponse.json(
      { error: 'Internal server error' },
      { status: 500 }
    );
  }
}

