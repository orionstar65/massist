'use client';

import { useState, useEffect } from 'react';
import { Prompt } from '@/lib/types';

interface PromptFormProps {
  prompt?: Prompt | null;
  onSave: (data: { name: string; prompt: string; description?: string; is_default?: boolean }) => void;
  onCancel: () => void;
}

export default function PromptForm({ prompt, onSave, onCancel }: PromptFormProps) {
  const [name, setName] = useState('');
  const [promptText, setPromptText] = useState('');
  const [description, setDescription] = useState('');
  const [isDefault, setIsDefault] = useState(false);

  useEffect(() => {
    if (prompt) {
      setName(prompt.name);
      setPromptText(prompt.prompt);
      setDescription(prompt.description || '');
      setIsDefault(prompt.is_default || false);
    }
  }, [prompt]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSave({
      name,
      prompt: promptText,
      description: description || undefined,
      is_default: isDefault,
    });
  };

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
      <div className="bg-dark-surface border border-dark-border rounded-lg p-6 w-full max-w-2xl max-h-[90vh] overflow-y-auto">
        <h2 className="text-xl font-bold text-dark-text mb-4">
          {prompt ? 'Edit Prompt' : 'Create New Prompt'}
        </h2>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label htmlFor="name" className="block text-sm font-medium text-dark-text mb-2">
              Name *
            </label>
            <input
              id="name"
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              className="w-full px-4 py-2 bg-dark-bg border border-dark-border rounded text-dark-text focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="e.g., Professional, STAR, Funny"
            />
          </div>

          <div>
            <label htmlFor="description" className="block text-sm font-medium text-dark-text mb-2">
              Description
            </label>
            <input
              id="description"
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="w-full px-4 py-2 bg-dark-bg border border-dark-border rounded text-dark-text focus:outline-none focus:ring-2 focus:ring-blue-500"
              placeholder="Optional description"
            />
          </div>

          <div>
            <label htmlFor="prompt" className="block text-sm font-medium text-dark-text mb-2">
              Prompt Text *
            </label>
            <textarea
              id="prompt"
              value={promptText}
              onChange={(e) => setPromptText(e.target.value)}
              required
              rows={8}
              className="w-full px-4 py-2 bg-dark-bg border border-dark-border rounded text-dark-text focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
              placeholder="Enter the prompt text..."
            />
          </div>

          <div className="flex items-center">
            <input
              id="is_default"
              type="checkbox"
              checked={isDefault}
              onChange={(e) => setIsDefault(e.target.checked)}
              className="w-4 h-4 text-blue-600 bg-dark-bg border-dark-border rounded focus:ring-blue-500"
            />
            <label htmlFor="is_default" className="ml-2 text-sm text-dark-text">
              Set as default
            </label>
          </div>

          <div className="flex gap-2 justify-end">
            <button
              type="button"
              onClick={onCancel}
              className="px-4 py-2 bg-gray-600 hover:bg-gray-700 text-white rounded"
            >
              Cancel
            </button>
            <button
              type="submit"
              className="px-4 py-2 bg-blue-600 hover:bg-blue-700 text-white rounded"
            >
              {prompt ? 'Update' : 'Create'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

