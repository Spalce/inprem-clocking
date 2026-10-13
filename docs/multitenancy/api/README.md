# Website API description

`public-v1.draft.yaml` is the **draft** OpenAPI 3 description of the website API, written in G0 from design v0.5, section 7. It is documentation only; no code implements it yet.

Phase 3 builds the API and generates the real description from code (`/api/public/docs` on staging). A contract test then checks the code against the agreed version of this file. Open questions are listed at the end of `info.description` in the YAML.

## Validate

```bash
docker run --rm -v "$PWD/docs/multitenancy/api:/work" -w /work stoplight/spectral:6.14.2 \
  lint public-v1.draft.yaml --ruleset .spectral.yaml --fail-severity warn
docker run --rm -v "$PWD/docs/multitenancy/api:/spec" redocly/cli:1.25.11 lint /spec/public-v1.draft.yaml
```

Both pass as of 2026-10-13. Redocly's one remaining warning is the missing `info.license`, left out until a licence is chosen.

To browse the draft, open it in any OpenAPI viewer, for example `docker run -p 8080:8080 -e SWAGGER_JSON=/spec/public-v1.draft.yaml -v "$PWD/docs/multitenancy/api:/spec" swaggerapi/swagger-ui`.
