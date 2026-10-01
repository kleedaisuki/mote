# Small YAML skipped-key certificate review

## Scope and verdict

Reviewed frozen production commit
`030e56984b4d07d902c4670fcf9cb4071c306a3e`, against its parent
`afb7b30268f7719a07019439d1572d3f606a7991`. Source:
`src/Mote.Formats/YamlPolicy.cs`, CRLF working-file SHA-256
`E913D834EF59B55367D9AE4339CB82AFA2FD27356FC5B230EE5477D8799E5A67`.
The production diff adds one warning inside the existing skipped-key branch;
the session and common APIs are unchanged.

**No substantive defect found in this bounded source review.** A skipped key
comparison now exposes the missing uniqueness proof to the existing small
session completeness classifier. This is recovery-certificate correction, not
an exhaustive malformed-YAML diagnosis or span repair.

## Independent source-level proof

1. `YamlPolicy.Projector.CheckMappingKeys` (lines 302–314) retains exactly the
   former error-containment predicate and `continue`. A key with an existing
   contained error is still not canonicalized or entered in the uniqueness set.
   Consequently the change cannot invent a duplicate comparison from an invalid
   key. The new warning uses the existing `yaml.key-equality-unsupported` code,
   the key span and the established message prefix, with a closed failure reason.
2. Existing errors are neither removed nor rewritten. The new warning is not an
   error, so it cannot change any subsequent evaluation of the error-only skip
   predicate. Relative ordering of previously produced records is preserved;
   the intentionally added warning is interleaved at the skipped comparison.
   The branch remains a loop `continue`, not a return from the mapping or tree:
   later independent keys and recursive child checks still execute.
3. `YamlIncrementalSession.AnalyzeSmall` (lines 70–86) computes `trustworthy`
   from **all** policy diagnostics, before viewport filtering or output limits.
   Therefore an offscreen skipped key downgrades both Full and Visible requests.
   The existing unsupported-code check makes completeness `Provisional`, total
   diagnostic count unknown, and coverage the requested visible range. No
   renderer-specific interpretation or new session state is needed.
4. An error in an ordinary value does not satisfy its enclosing mapping key's
   containment predicate. Thus a plain invalid scalar or undefined alias value
   alone remains intentionally Complete: an error does not itself imply an
   incomplete scan. If that value contains its own skipped mapping key, that
   separate warning legitimately invalidates the whole-document certificate.
5. Aliases to erroneous anchored values, custom tags, cycles, comparison depth
   and syntax exceptions retain their existing independent unsupported/error
   paths. No change to anchor binding, memoization, scalar canonicalization,
   tokens or semantic-node construction occurred. Format still refuses sources
   with the existing error records; adding a warning cannot make such a source
   eligible for rewriting.

The semantic obligation is grounded in [YAML 1.2.2 section
3.2.1.3](https://yaml.org/spec/1.2.2/#3213-node-comparison): mappings require key
uniqueness and node equality depends on tag and canonical content. Retaining
unknown uniqueness after a deliberately skipped comparison is more accurate
than claiming the absence of duplicate diagnostics proves completion.

## Review limits and negative boundaries

This review independently inspected the source diff, complete key-check loop,
and small-session classifier/filtering order. It did not rerun the previous
153-case regression or the separately owned baseline/candidate fixture tests.
No discovered defect warranted a new reproduction. Build and runtime results
belong to their actual worker/validator artifacts, not this source review.

The prior span-containment predicate is not redesigned here. In particular,
flow-collection spans still omit a closing delimiter in characterized cases;
this change does not repair that inherited projection issue. Syntax parsing can
still stop at its first exception, already producing a provisional certificate.
Large-file streaming behavior, bounded fallback, cancellation timing, and
native GUI presentation are outside this patch's verification scope.
