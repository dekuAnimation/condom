import { createClient } from 'npm:@supabase/supabase-js@2.57.4';

// JWT verification is disabled because each PC has a separate, revocable,
// high-entropy read-only token. No service credential ever leaves this function.
Deno.serve(async (request: Request) => {
  const response = (status: number, body: unknown) => new Response(JSON.stringify(body), {
    status, headers: { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' },
  });
  if (request.method !== 'POST') return response(405, { error: 'POST required' });
  const token = request.headers.get('X-Device-Token') || '';
  if (!/^[0-9a-f]{64}$/.test(token)) return response(401, { error: 'Invalid device' });
  try {
    const hash = Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(token))), b => b.toString(16).padStart(2, '0')).join('');
    const client = createClient(Deno.env.get('SUPABASE_URL')!, Deno.env.get('SUPABASE_SERVICE_ROLE_KEY')!, {
      auth: { persistSession: false, autoRefreshToken: false },
    });
    const { data, error } = await client.rpc('screen_manifest', { p_hash: hash });
    if (error) throw error;
    if (!data) return response(401, { error: 'Invalid or revoked device' });
    const slides = [];
    for (const slide of data.slides) {
      const { data: signed, error: signError } = await client.storage.from('notices').createSignedUrl(slide.path, 600);
      if (signError) throw signError;
      slides.push({ id: slide.id, title: slide.title, sha256: slide.sha256, url: signed.signedUrl });
    }
    return response(200, { name: data.name, interval_seconds: 30, slides });
  } catch {
    return response(503, { error: 'Manifest temporarily unavailable' });
  }
});
