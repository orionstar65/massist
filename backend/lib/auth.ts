import jwt from 'jsonwebtoken';
import { query } from './db';
import { User } from './types';

const JWT_SECRET = process.env.JWT_SECRET || 'default-secret-change-in-production';
const JWT_EXPIRES_IN = process.env.JWT_EXPIRES_IN || '7d';

export interface JWTPayload {
  userId: number;
  username: string;
}

export function generateToken(user: { id: number; username: string }): string {
  const payload: JWTPayload = {
    userId: user.id,
    username: user.username,
  };

  return jwt.sign(payload, JWT_SECRET, {
    expiresIn: JWT_EXPIRES_IN,
  });
}

export function verifyToken(token: string): JWTPayload | null {
  try {
    const decoded = jwt.verify(token, JWT_SECRET) as JWTPayload;
    return decoded;
  } catch (error) {
    return null;
  }
}

export async function getUserFromToken(token: string): Promise<User | null> {
  const payload = verifyToken(token);
  if (!payload) {
    return null;
  }

  try {
    const users = await query<User[]>(
      'SELECT id, username, password_hash, created_at, updated_at FROM users WHERE id = ?',
      [payload.userId]
    );

    if (users.length === 0) {
      return null;
    }

    return users[0];
  } catch (error) {
    console.error('Error getting user from token:', error);
    return null;
  }
}

export function getAuthTokenFromRequest(request: Request): string | null {
  const authHeader = request.headers.get('authorization');
  if (authHeader && authHeader.startsWith('Bearer ')) {
    return authHeader.substring(7);
  }
  return null;
}

export async function requireAuth(request: Request): Promise<{ user: User; payload: JWTPayload } | null> {
  const token = getAuthTokenFromRequest(request);
  if (!token) {
    return null;
  }

  const user = await getUserFromToken(token);
  if (!user) {
    return null;
  }

  const payload = verifyToken(token);
  if (!payload) {
    return null;
  }

  return { user, payload };
}

