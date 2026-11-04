import { check, sleep } from 'k6';
import http from 'k6/http';
import { uuidv4, randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

// The token is never written here: run with `k6 run -e TOKEN=<access token> -e TENANT=<tenant id> load-rest.js`.
const params = {
    headers: {
        'Content-Type': 'application/json',
        'X-Tenant': __ENV.TENANT,
        'Authorization': `Bearer ${__ENV.TOKEN}`
    },
};

export const options = {
    discardResponseBodies: true,
    scenarios: {
        contacts: {
            executor: 'constant-arrival-rate',
            duration: '600s',

            // How many iterations per timeUnit
            rate: 10,

            // Start `rate` iterations per second
            timeUnit: '1s',

            // Pre-allocate 2 VUs before starting the test
            preAllocatedVUs: 3,

            // Spin up a maximum of 50 VUs to sustain the defined
            // constant arrival rate.
            maxVUs: 5000,
        },
    },
};

export default async function () {

    const document = JSON.stringify({
        "id": uuidv4(),
        "reference": `load:document:${randomIntBetween(1, 1000000)}`
    });

    let response = await http.asyncRequest('POST', 'http://localhost:5000/api/ElectronicDocument', document, params);

    check(response, {
        'is status 204': (r) => r.status === 204,
    });

    sleep(1);
}
