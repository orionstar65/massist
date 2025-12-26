'use client';

import { useState, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { Prompt } from '@/lib/types';
import PromptCard from './components/PromptCard';
import PromptForm from './components/PromptForm';

export default function Dashboard() {
  const router = useRouter();
  const [prompts, setPrompts] = useState<Prompt[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editingPrompt, setEditingPrompt] = useState<Prompt | null>(null);
  const [cloningPromptId, setCloningPromptId] = useState<number | null>(null);

  useEffect(() => {
    checkAuth();
    fetchPrompts();
  }, []);

  const checkAuth = () => {
    const token = localStorage.getItem('authToken');
    if (!token) {
      router.push('/login');
      return;
    }
  };

  const fetchPrompts = async () => {
    const token = localStorage.getItem('authToken');
    if (!token) {
      router.push('/login');
      return;
    }

    try {
      const response = await fetch('/api/prompts', {
        headers: {
          'Authorization': `Bearer ${token}`,
        },
      });

      if (response.status === 401) {
        localStorage.removeItem('authToken');
        localStorage.removeItem('user');
        router.push('/login');
        return;
      }

      const data = await response.json();
      setPrompts(data.prompts || []);
    } catch (err) {
      setError('Failed to load prompts');
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = () => {
    setEditingPrompt(null);
    setShowForm(true);
  };

  const handleEdit = (prompt: Prompt) => {
    setEditingPrompt(prompt);
    setShowForm(true);
  };

  const handleSave = async (formData: { name: string; prompt: string; description?: string; is_default?: boolean }) => {
    const token = localStorage.getItem('authToken');
    if (!token) return;

    try {
      const url = editingPrompt ? `/api/prompts/${editingPrompt.id}` : '/api/prompts';
      const method = editingPrompt ? 'PUT' : 'POST';

      const response = await fetch(url, {
        method,
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`,
        },
        body: JSON.stringify(formData),
      });

      if (!response.ok) {
        const data = await response.json();
        alert(data.error || 'Failed to save prompt');
        return;
      }

      setShowForm(false);
      setEditingPrompt(null);
      fetchPrompts();
    } catch (err) {
      alert('Failed to save prompt');
    }
  };

  const handleDelete = async (id: number) => {
    if (!confirm('Are you sure you want to delete this prompt?')) {
      return;
    }

    const token = localStorage.getItem('authToken');
    if (!token) return;

    try {
      const response = await fetch(`/api/prompts/${id}`, {
        method: 'DELETE',
        headers: {
          'Authorization': `Bearer ${token}`,
        },
      });

      if (!response.ok) {
        const data = await response.json();
        alert(data.error || 'Failed to delete prompt');
        return;
      }

      fetchPrompts();
    } catch (err) {
      alert('Failed to delete prompt');
    }
  };

  const handleClone = async (id: number) => {
    const promptToClone = prompts.find(p => p.id === id);
    if (!promptToClone) return;

    const newName = `${promptToClone.name} (Copy)`;
    const userInput = window.prompt(`Enter a name for the cloned prompt:`, newName);
    
    if (!userInput) return;

    setCloningPromptId(id);
    const token = localStorage.getItem('authToken');
    if (!token) return;

    try {
      const response = await fetch(`/api/prompts/${id}/clone`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${token}`,
        },
        body: JSON.stringify({ name: userInput }),
      });

      if (!response.ok) {
        const data = await response.json();
        alert(data.error || 'Failed to clone prompt');
        return;
      }

      fetchPrompts();
    } catch (err) {
      alert('Failed to clone prompt');
    } finally {
      setCloningPromptId(null);
    }
  };

  const handleAdopt = async (id: number) => {
    const token = localStorage.getItem('authToken');
    if (!token) return;

    try {
      const response = await fetch(`/api/prompts/${id}/adopt`, {
        method: 'POST',
        headers: {
          'Authorization': `Bearer ${token}`,
        },
      });

      if (!response.ok) {
        const data = await response.json();
        alert(data.error || 'Failed to adopt prompt');
        return;
      }

      fetchPrompts();
    } catch (err) {
      alert('Failed to adopt prompt');
    }
  };

  const handleRemove = async (id: number) => {
    if (!confirm('Remove this prompt from your collection?')) {
      return;
    }

    const token = localStorage.getItem('authToken');
    if (!token) return;

    try {
      const response = await fetch(`/api/prompts/${id}/adopt`, {
        method: 'DELETE',
        headers: {
          'Authorization': `Bearer ${token}`,
        },
      });

      if (!response.ok) {
        const data = await response.json();
        alert(data.error || 'Failed to remove prompt');
        return;
      }

      fetchPrompts();
    } catch (err) {
      alert('Failed to remove prompt');
    }
  };

  const handleLogout = () => {
    localStorage.removeItem('authToken');
    localStorage.removeItem('user');
    router.push('/login');
  };

  if (loading) {
    return (
      <div className="min-h-screen bg-dark-bg flex items-center justify-center">
        <p className="text-dark-text">Loading...</p>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-dark-bg">
      <header className="bg-dark-surface border-b border-dark-border">
        <div className="max-w-7xl mx-auto px-4 py-4 flex justify-between items-center">
          <h1 className="text-2xl font-bold text-dark-text">Prompt Management</h1>
          <div className="flex gap-4 items-center">
            <button
              onClick={handleCreate}
              className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded"
            >
              Create Prompt
            </button>
            <button
              onClick={handleLogout}
              className="px-4 py-2 bg-gray-600 hover:bg-gray-700 text-white rounded"
            >
              Logout
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-7xl mx-auto px-4 py-8">
        {error && (
          <div className="mb-4 p-3 bg-red-900/30 border border-red-700 rounded text-red-300">
            {error}
          </div>
        )}

        {prompts.length === 0 ? (
          <div className="text-center py-12">
            <p className="text-dark-text-muted mb-4">No prompts yet. Create your first prompt!</p>
            <button
              onClick={handleCreate}
              className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded"
            >
              Create Prompt
            </button>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {prompts.map((prompt) => (
              <PromptCard
                key={prompt.id}
                prompt={prompt}
                onEdit={handleEdit}
                onDelete={handleDelete}
                onClone={handleClone}
                onAdopt={handleAdopt}
                onRemove={handleRemove}
              />
            ))}
          </div>
        )}
      </main>

      {showForm && (
        <PromptForm
          prompt={editingPrompt}
          onSave={handleSave}
          onCancel={() => {
            setShowForm(false);
            setEditingPrompt(null);
          }}
        />
      )}
    </div>
  );
}

