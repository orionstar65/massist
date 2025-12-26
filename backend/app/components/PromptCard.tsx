'use client';

import { Prompt } from '@/lib/types';

interface PromptCardProps {
  prompt: Prompt;
  onEdit: (prompt: Prompt) => void;
  onDelete: (id: number) => void;
  onClone: (id: number) => void;
  onAdopt: (id: number) => void;
  onRemove: (id: number) => void;
}

export default function PromptCard({ prompt, onEdit, onDelete, onClone, onAdopt, onRemove }: PromptCardProps) {
  const isOwned = prompt.is_owned === 1 || prompt.is_owned === true;
  const isAdopted = prompt.is_adopted === 1 || prompt.is_adopted === true;

  return (
    <div className="bg-dark-surface border border-dark-border rounded-lg p-4 hover:border-blue-500 transition-colors">
      <div className="flex justify-between items-start mb-2">
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <h3 className="text-lg font-semibold text-dark-text">{prompt.name}</h3>
            {isOwned && (
              <span className="px-2 py-1 text-xs bg-green-900/30 text-green-300 rounded">
                Owned
              </span>
            )}
            {!isOwned && isAdopted && (
              <span className="px-2 py-1 text-xs bg-blue-900/30 text-blue-300 rounded">
                Adopted
              </span>
            )}
            {!isOwned && !isAdopted && (
              <span className="px-2 py-1 text-xs bg-gray-700 text-gray-300 rounded">
                Available
              </span>
            )}
          </div>
          {prompt.description && (
            <p className="text-sm text-dark-text-muted mt-1">{prompt.description}</p>
          )}
          {prompt.created_by_username && (
            <p className="text-xs text-dark-text-muted mt-1">
              Created by: {prompt.created_by_username}
            </p>
          )}
          {prompt.is_default && (
            <span className="inline-block mt-2 px-2 py-1 text-xs bg-blue-900/30 text-blue-300 rounded">
              Default
            </span>
          )}
        </div>
      </div>

      <div className="mt-4 p-3 bg-dark-bg rounded border border-dark-border">
        <p className="text-sm text-dark-text-muted line-clamp-3">{prompt.prompt}</p>
      </div>

      <div className="mt-4 flex gap-2 flex-wrap">
        {isOwned && (
          <>
            <button
              onClick={() => onEdit(prompt)}
              className="px-3 py-1.5 text-sm bg-blue-600 hover:bg-blue-700 text-white rounded"
            >
              Edit
            </button>
            <button
              onClick={() => onDelete(prompt.id)}
              className="px-3 py-1.5 text-sm bg-red-600 hover:bg-red-700 text-white rounded"
            >
              Delete
            </button>
          </>
        )}
        {!isOwned && (
          <>
            {!isAdopted ? (
              <button
                onClick={() => onAdopt(prompt.id)}
                className="px-3 py-1.5 text-sm bg-green-600 hover:bg-green-700 text-white rounded"
              >
                Adopt
              </button>
            ) : (
              <button
                onClick={() => onRemove(prompt.id)}
                className="px-3 py-1.5 text-sm bg-orange-600 hover:bg-orange-700 text-white rounded"
              >
                Remove
              </button>
            )}
          </>
        )}
        <button
          onClick={() => onClone(prompt.id)}
          className="px-3 py-1.5 text-sm bg-purple-600 hover:bg-purple-700 text-white rounded"
        >
          Clone
        </button>
      </div>
    </div>
  );
}

