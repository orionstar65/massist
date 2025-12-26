/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './pages/**/*.{js,ts,jsx,tsx,mdx}',
    './components/**/*.{js,ts,jsx,tsx,mdx}',
    './app/**/*.{js,ts,jsx,tsx,mdx}',
  ],
  theme: {
    extend: {
      colors: {
        dark: {
          bg: '#1a1a1a',
          surface: '#252525',
          border: '#333',
          text: '#e0e0e0',
          'text-muted': '#b0b0b0',
        },
      },
    },
  },
  plugins: [],
  darkMode: 'class',
}

