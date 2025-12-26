# Power Interview Assistant - Backend Server

Backend server for managing interview prompts with authentication.

## Features

- User authentication (username/password with JWT tokens)
- Prompt CRUD operations
- Prompt cloning
- MySQL database
- Next.js 14 with App Router
- Dark theme UI with Tailwind CSS
- RESTful API

## Prerequisites

- Node.js 18+ 
- MySQL 8.0+
- npm or yarn

## Setup

1. **Install dependencies:**
   ```bash
   npm install
   ```

2. **Set up MySQL database:**
   ```bash
   mysql -u root -p < database/schema.sql
   ```

3. **Configure environment variables:**
   Create a `.env.local` file in the backend directory:
   ```env
   DATABASE_URL=mysql://username:password@localhost:3306/power_interview_assistant
   JWT_SECRET=your-secret-key-change-this-in-production
   JWT_EXPIRES_IN=7d
   PORT=3000
   NEXT_PUBLIC_API_URL=http://localhost:3000
   ```

4. **Run the development server:**
   ```bash
   npm run dev
   ```

   The server will start on http://localhost:3000

## API Endpoints

### Authentication

- `POST /api/auth/register` - Register new user
- `POST /api/auth/login` - Login
- `POST /api/auth/logout` - Logout
- `GET /api/auth/me` - Get current user (requires auth)

### Prompts (all require authentication)

- `GET /api/prompts` - Get all prompts for authenticated user
- `POST /api/prompts` - Create new prompt
- `GET /api/prompts/:id` - Get specific prompt
- `PUT /api/prompts/:id` - Update prompt
- `DELETE /api/prompts/:id` - Delete prompt
- `POST /api/prompts/:id/clone` - Clone prompt

## Web UI

- `/login` - Login page
- `/register` - Registration page
- `/` - Dashboard (requires authentication)

## Database Schema

- `users` - User accounts with password hashes
- `prompts` - Prompt definitions
- `user_prompts` - Many-to-many mapping between users and prompts

## Local Network Access

The server is configured to accept connections from local network IPs. Make sure to:

1. Update `NEXT_PUBLIC_API_URL` in `.env.local` if needed
2. Configure firewall to allow connections on port 3000
3. Use the server's local IP address (e.g., `http://192.168.1.100:3000`) in the Electron app

## Production Deployment

For production:

1. Set a strong `JWT_SECRET`
2. Use environment-specific database credentials
3. Configure proper CORS settings
4. Use HTTPS in production
5. Set up proper error logging and monitoring

