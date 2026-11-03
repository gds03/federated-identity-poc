/check decodes the token with ReadJwtToken and never verifies the signature. In CentralCheck mode, anyone could forge a token or send an X-Client-Id header naming a privileged client. Route-level control means little until /check validates the signature against the JWKS. I'd fix that next


the data model use ef core with sqlite? - but when we have like 100 APIs each using check to confirm authorization on each request, what db would we switch for this to scale, can you re-implement?