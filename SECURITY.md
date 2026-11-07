# V1 security limitations

V1 is a local portfolio demonstration for trusted users and synthetic data.
It is **not production-ready for untrusted public exposure**. Bind the API and
demo database to loopback as shown in the [README](README.md).

## Access and evidence trust

There is no application authentication, authorization, role model, or tenant
isolation. Anyone able to reach the API can create services and releases, submit
evidence, and retrieve releases whose IDs they know. An ID is not an access
control. There is no application rate limiting or per-user quota.

Validation evidence is manually submitted. The application does not independently
execute tests, deploy software, monitor deployment health, or verify evidence.
It records no authenticated submitter identity, signature, or tamper-evident audit
trail. `Verified` means that three stored outcomes say `Passed`.

The first accepted result for a release/type is retained. An unauthenticated writer
could therefore submit false evidence or occupy a validation type before a trusted
user. API immutability does not prevent a database operator from changing data.

`evidenceUrl` is length-limited text; the application does not validate its scheme,
fetch it, or verify its content. Treat returned links and summaries as untrusted.
Evidence links may change, expire, or expose sensitive material outside this API.
Do not submit credentials, signed access URLs, confidential reports, or client data.

## Local operation

The documented demo uses HTTP on loopback. The application does not enforce HTTPS
or configure a public TLS boundary. A development HTTPS launch profile is not a
public deployment security design. Database transport security and credentials
depend on the supplied connection string and environment.

Keep connection strings outside tracked files. Environment variables or local
.NET user secrets can supply configuration; user secrets are a development
convenience, not an encrypted production secret store. Docker container configuration
is visible to users with Docker access. Demo passwords and CI fixture credentials
must never be reused outside their disposable local/test databases.

Required-field checks, string-length limits, database constraints, and generic
unexpected-error responses are implemented. These measures do not provide the
missing identity and evidence-trust controls. This documentation review is not a
penetration test, dependency vulnerability audit, or production security approval.

## Reporting a concern

No dedicated private security reporting address or response commitment is currently
published. If the repository host offers private vulnerability reporting, use it;
otherwise ask the owner for a private contact route without posting exploit details,
credentials, or confidential evidence in a public issue. Do not assume the host's
private reporting feature is enabled.
