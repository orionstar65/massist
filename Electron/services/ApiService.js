// API Service for backend communication
class ApiService {
  constructor() {
    this.baseUrl = null;
    this.token = null;
  }

  setConfig(serverUrl, token) {
    this.baseUrl = serverUrl?.replace(/\/$/, ''); // Remove trailing slash
    this.token = token;
  }

  getHeaders() {
    const headers = {
      'Content-Type': 'application/json',
    };
    if (this.token) {
      headers['Authorization'] = `Bearer ${this.token}`;
    }
    return headers;
  }

  async request(endpoint, options = {}) {
    if (!this.baseUrl) {
      throw new Error('Server URL not configured');
    }

    const url = `${this.baseUrl}${endpoint}`;
    const config = {
      ...options,
      headers: {
        ...this.getHeaders(),
        ...(options.headers || {}),
      },
    };

    try {
      const response = await fetch(url, config);
      const data = await response.json();

      if (!response.ok) {
        if (response.status === 401) {
          // Token expired or invalid
          throw new Error('UNAUTHORIZED');
        }
        throw new Error(data.error || `HTTP ${response.status}`);
      }

      return data;
    } catch (error) {
      if (error.message === 'UNAUTHORIZED') {
        throw error;
      }
      if (error.name === 'TypeError' && error.message.includes('fetch')) {
        throw new Error('Network error: Could not connect to server');
      }
      throw error;
    }
  }

  async login(username, password) {
    const data = await this.request('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    });
    return data;
  }

  async register(username, password) {
    const data = await this.request('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    });
    return data;
  }

  async verifyToken() {
    const data = await this.request('/api/auth/me', {
      method: 'GET',
    });
    return data;
  }

  async fetchPrompts() {
    const data = await this.request('/api/prompts', {
      method: 'GET',
    });
    return data.prompts || [];
  }

  async createPrompt(promptData) {
    const data = await this.request('/api/prompts', {
      method: 'POST',
      body: JSON.stringify(promptData),
    });
    return data;
  }

  async updatePrompt(id, promptData) {
    const data = await this.request(`/api/prompts/${id}`, {
      method: 'PUT',
      body: JSON.stringify(promptData),
    });
    return data;
  }

  async deletePrompt(id) {
    const data = await this.request(`/api/prompts/${id}`, {
      method: 'DELETE',
    });
    return data;
  }

  async clonePrompt(id, name) {
    const data = await this.request(`/api/prompts/${id}/clone`, {
      method: 'POST',
      body: JSON.stringify({ name }),
    });
    return data;
  }
}

// Export singleton instance
module.exports = new ApiService();

