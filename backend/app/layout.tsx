import type { Metadata } from 'next'
import './globals.css'

export const metadata: Metadata = {
  title: 'Power Interview Assistant - Prompt Management',
  description: 'Manage your interview prompts',
}

export default function RootLayout({
  children,
}: {
  children: React.ReactNode
}) {
  return (
    <html lang="en" className="dark">
      <body className="bg-dark-bg text-dark-text">{children}</body>
    </html>
  )
}

