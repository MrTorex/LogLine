# Contributing to LogLine

Thank you for your interest in contributing to LogLine! 

LogLine is a zero-allocation, high-performance logging framework. Because of this, every contribution is held to strict performance, memory, and architectural standards. 

Before contributing, please read our [Code of Conduct](CODE_OF_CONDUCT.md) and review the guidelines below.

---

## Generative AI & LLM Policy

We follow a strict **"Human-in-the-Loop / Don't be a meat proxy"** policy:

1. **You own your code:** If you use an LLM to assist in writing code, you are 100% responsible for understanding every single instruction, line of memory management, and edge-case behavior.
2. **No raw AI slop:** Do not use LLMs to generate rambling PR descriptions, issue comments, or commit messages. Write concisely and clearly in your own words.
3. **No "proof-of-concept" dumps:** Do not submit unpolished or broken LLM drafts expecting maintainers to debug, profile, or finish them for you.
4. **No free advertisement:** Do not add "Assisted-by: insert_your_goofy_LLM_name" tags to your commits; it's just free advertising for the LLM's provider.

---

## Unity Version Support Policy

LogLine maintains official compatibility with:
* The **latest Active non-LTS** release.
* All **LTS releases** throughout their **entire 3-year Extended LTS period** (2 years standard LTS + 1 year Extended LTS) **plus 1 additional year** after Extended LTS ends.

> **Current Baseline:** As of October 2026, the minimum supported version is **Unity 2022.3 LTS** (scheduled for end-of-life around May/June 2027).

Any PR introducing features/refactors/etc. must work properly and pass all tests on the minimum supported LTS version without errors.

---

## Code Quality & Style Guidelines

* **Follow `.editorconfig`:** The repository includes a `.editorconfig` file defining our formatting rules (indentation, braces, spacing, naming conventions). Ensure your IDE/editor respects these settings. Do not submit mass reformatting of unrelated files.
* **Match Existing Comment Styles:** Write XML documentation (`/// <summary>`). Сode comments should match the tone and formatting found in the codebase: explain the *intent*, subtle edge cases, or low-level memory tricks, rather than describing obvious lines of code.
* **Zero-Allocation:** Critical paths must produce 0 B of GC allocations (no boxing, no closure captures, no hidden LINQ). Any unavoidable allocations must be clearly justified and documented.

---

## How to Contribute

### 1. Reporting Bugs and Issues
* Check the existing [Issues](../../issues) to verify the problem hasn't already been reported.
* Use the provided **Issue Templates**.
* Be clear, concise, and technical.

### 2. Workflow & Branching Strategy

We use a standard two-branch lifecycle:

* **`development`** — All active work, feature branches, and fixes are merged here. **Target your Pull Requests to this branch.**
* **`main`** — Production-ready, stable releases. Maintainers merge `development` into `main` only when a formal release is published and tagged.

### 3. Submitting a Pull Request

1. **Fork & Clone:** Fork the repository to your account and clone it locally.
2. **Create a branch:** Branch off from `development`:
   ```bash
   git checkout -b feature/my-super-improvement origin/development
3. Make your changes.
4. **Submit a pull request:** Submit a pull request at [this page](https://github.com/MrTorex/LogLine/compare).
5. **Wait for approval:** Pat your self on the back and wait for your pull request to be reviewed.

If you're unfamiliar with how pull requests work, [GitHub's documentation on them](https://help.github.com/articles/using-pull-requests/) is very good.

Here are a few things you can do that will increase the likelihood of your pull request being accepted:

* Update the documentation as necessary, as well as making code changes.
* Keep your change as focused as possible. If there are multiple changes you would like to make that are not dependent upon each other, consider submitting them as separate pull requests.

---

### Code and other contributions

Contributions to LogLine (via pull request or otherwise) must be licensed under the [MIT license](LICENSE.md).
