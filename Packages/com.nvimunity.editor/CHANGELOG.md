# Changelog

All notable changes to this package are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and this package adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- csproj generation for package `Samples~` code. Unity never imports `Samples~`, so sample
  scripts previously landed in no project and Roslyn reported every Unity type as missing.
  Each sample asmdef (or asmdef-less sample folder) now gets a synthesized csproj in the sln.

## [1.0.0] - 2026-10-07

### Added
- Initial release.
