export interface User {
  id: number;
  username: string;
  password_hash: string;
  created_at: Date;
  updated_at: Date;
}

export interface Prompt {
  id: number;
  name: string;
  prompt: string;
  description?: string;
  is_default: boolean;
  created_by: number;
  created_by_username?: string;
  is_owned: boolean; // Whether current user owns this prompt
  is_adopted: boolean; // Whether current user has adopted this prompt
  created_at: Date;
  updated_at: Date;
}

export interface UserPrompt {
  id: number;
  user_id: number;
  prompt_id: number;
  created_at: Date;
}

export interface AuthResponse {
  user: {
    id: number;
    username: string;
  };
  token: string;
}

export interface ApiError {
  error: string;
  message?: string;
}

