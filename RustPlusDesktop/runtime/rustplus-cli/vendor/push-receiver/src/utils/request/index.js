const axios = require('axios');
const { waitFor } = require('../timeout');

// In seconds
const MAX_RETRY_TIMEOUT = 15;
// Step in seconds
const RETRY_STEP = 5;

module.exports = requestWithRety;

function requestWithRety(...args) {
  return retry(0, ...args);
}

async function retry(retryCount = 0, ...args) {
  try {
    const result = await request(...args);
    return result;
  } catch (e) {
    const timeout = Math.min(retryCount * RETRY_STEP, MAX_RETRY_TIMEOUT);
    console.error(`Request failed : ${e.message}`);
    console.error(`Retrying in ${timeout} seconds`);
    await waitFor(timeout * 1000);
    const result = await retry(retryCount + 1, ...args);
    return result;
  }
}

async function request(options) {
  const url = options.url || options.uri;
  const method = options.method || 'GET';
  const headers = Object.assign({}, options.headers);
  let data = options.body;

  if (options.form) {
    data = new URLSearchParams(options.form).toString();
    if (!headers['Content-Type'] && !headers['content-type']) {
      headers['Content-Type'] = 'application/x-www-form-urlencoded';
    }
  }

  const response = await axios({
    url,
    method,
    headers,
    data,
    responseType: options.encoding === null ? 'arraybuffer' : 'text',
    transformResponse: value => value,
  });

  if (options.encoding === null) {
    return Buffer.from(response.data);
  }

  if (typeof response.data === 'string') {
    return response.data;
  }

  return Buffer.from(response.data).toString(options.encoding || 'utf8');
}
