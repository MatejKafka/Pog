# Interesting questions that I had to deal with when designing Pkg

## Single-manifest-per-version vs single-manifest-per-package

2 general approaches to publishing new versions of packages:

1. author creates a new manifest, changes the version, build steps,... and uploads the new manifest to repository; there is a separate manifest file for each version of a package
	- **Cons:** likely duplication of code between packages - they might be the same except for declared version
	- **Cons:** when an error is discovered in older manifest, it cannot be easily fixed; also, even if it were possible, fixes from new versions would have to be back-ported manually
	- **Possible improvement:** have manifest generator which lets package author generate manifests from a template, automating away much of the duplication
2. author updates a single manifest to support the new version of package; the manifest receives the version as an argument, and installs the appropriate version; repository is updated by overwriting previous package manifest
	- **Cons:** package author might accidentally break older package version
	- **Cons:** the manifest will have to know all supported versions and hashes for all resources

Possible compromise: have root resource file for each package, which manifests for each version import and call with version-specific parameters; resolves all mentioned **cons** except for accidental backwards compatibility breakage.