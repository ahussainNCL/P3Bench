// The published site serves static files; only the local app owns bridge access.
export const hosted = import.meta.env.VITE_HOSTED === "true";
export const studioUrl = hosted ? "http://127.0.0.1:8787/app" : "/app";
