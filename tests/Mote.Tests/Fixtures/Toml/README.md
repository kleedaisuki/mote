# TOML 1.1 conformance fixtures

`toml-test-1.1.jsonl` preserves the exact bytes of all 712 `.toml` entries in
`toml-lang/toml-test`'s `tests/files-toml-1.1.0` at commit
`ff49d109861c1ad25af53f687f2aef19ab650600`. The upstream MIT license is included.

Each record contains the upstream path, expected validity and base64 source bytes.
Valid JSON expected-value files are not included: these tests certify syntax and
namespace-ownership validity, **not decoded semantic-value equivalence**.
Nine invalid files contain invalid UTF-8; the strict fixture decoder classifies
those at its encoding boundary and does not credit the TOML analyzer with rejection.
The production statement algorithm is invoked directly to avoid adding >4 MiB
padding to each short file. Separate regressions exercise the real large-file
session dispatcher. The frozen published corpus is finite evidence, not a proof of
all TOML syntax or resource-free arbitrary-size handling.
