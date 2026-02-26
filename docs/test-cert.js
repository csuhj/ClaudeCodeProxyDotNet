const https = require('https');

const url = new URL(process.env.ANTHROPIC_BASE_URL);

const options = {
  hostname: url.hostname,
  port: url.port,
  path: '/index.html',
  method: 'GET',
};

console.log(`Testing connection to ${url.href}`);
console.log(`NODE_EXTRA_CA_CERTS: ${process.env.NODE_EXTRA_CA_CERTS || '(not set)'}`);
console.log('---');

const req = https.request(options, (res) => {
  console.log(`✓ Connected successfully`);
  console.log(`  Status: ${res.statusCode}`);
  console.log(`  Certificate CN: ${res.socket.getPeerCertificate()?.subject?.CN}`);
  console.log(`  Cert valid from: ${res.socket.getPeerCertificate()?.valid_from}`);
  console.log(`  Cert valid to:   ${res.socket.getPeerCertificate()?.valid_to}`);
  console.log(`  Authorized: ${res.socket.authorized}`);
});

req.on('error', (e) => {
  console.error(`✗ Connection failed: ${e.message}`);
  if (e.code) console.error(`  Error code: ${e.code}`);
});

req.end();