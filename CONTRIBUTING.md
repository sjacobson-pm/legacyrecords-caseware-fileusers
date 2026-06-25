# Contributing to the LegacyRecordsCaseWareFileUsers

See the [README](README.md) to get an overview of the project
To provide feedback, please follow the guidance in this document.

Use your best judgment and feel free to propose changes to anything in this repository, including these contribution guidelines.

## Creating Issues

- You can [create an issue](../../../issues/new/choose), but before doing that please read the bullets below and include as many details as possible.
- Perform a [cursory search](../../../issues) to see if a similar item has already been submitted.

<!-- todo: list out any related repositories below; if there are no related repositories, then remove this entire section -->
<!--
### Related repositories

This is the repository for LegacyRecordsCaseWareFileUsers.
Please ensure that you are opening issues in the correct repository.

    The following repositories are also related to LegacyRecordsCaseWareFileUsers:

- [repo-001][repo--repo-001]
- [repo-002][repo--repo-002]
-->

## Recommended setup for contributing

**NOTE:** Always be sure to use **_only_** approved versions from the [AppDev Workstation Configuration][repo--appdev-workstation-configuration] guide.

- Install Git and clone this repository
- Install nvm
- Install (via nvm) node v22.x
- Install .net core 8.x
- Install VS Code
- Install Visual Studio
- Install all recommended VS Code extensions (included in /.vscode/extensions.json)
- Install dependencies
  - `npm install`
  - `dotnet restore`

## Documentation style guide

- Please reference GitHub's [basic writing and formatting syntax][gh-md-syntax-guidance]
- Use syntax-highlighted examples liberally
- Write one sentence per line.

## Making changes

- Create a [topic branch][topic-branch] named appropriately (`<initials>-issue-<issue#>[-additional-info]`).
  - Branches for new features belong in the `feature/` folder.
  - Branches for bug fixes belong in the `fix/` folder.
  - examples
    - `feature/mob-issue-123`
    - `feature/daf-issue-456-add-cancel-button`
    - `fix/mob-issue-222`
    - `fix/daf-issue-333-fix-typo`
- Once you are ready with your changes, don't forget to self review to quicken the review process ⚡.
  - [ ] 🔒 All commits **MUST** be properly signed.
  - [ ] ✔️ Confirm that the changes meet the user experience and goals laid out in the issue/requirements/etc.
  - [ ] 🧪 You've **tested your changes locally** (run/execute the code) to confirm desired functionality.
  - [ ] 📝 Ensure any documentation within the code is updated. (README, CONTRIBUTING, etc.)
  - [ ] 🔍 Review the changes for grammar, spelling, etc.
  - [ ] 🎨 All code, markdown, etc. is properly formatted, linted, etc.
  - [ ] 🧪 Unit tests are added/updated for changed code.
  - [ ] 🧪 Unit tests are added for all new code.
  - [ ] ✅ All unit tests pass.
  - [ ] 💯 Code coverage is at 100%.
  - [ ] etc.

## Commit messages

- Keep commit messages concise; limit the first line to 70 characters or less.
- Commit messages should begin with a capital letter and should not end with a period.
- Use the present tense. ("Add feature" not "Added feature")
- Use the imperative mood. ("Move cursor to..." not "Moves cursor to...")
- Add additional details (as needed) to subsequent lines (70 chars or less each), leaving an empty line after the first line.
- Reference issues and pull requests liberally after the first line as needed.

### EXAMPLES

#### POOR Examples

```text
changed the data types for field1, field2, and field3 on myCustomObject from integer to boolean so they can take up less space
```

In this example, the commit message is way too long and contains too much detail for a single-line commit message.

```text
data types are the wrong type
```

In this example, the commit message states a problem, not what is in this commit.
This type of information would be in the issue and does not belong in a commit message.

```text
commit change for adding the azureBlobsHelper module to this project
```

The word `commit` does not belong in a commit message.
This **is** a commit message, you don't need to state that this is a commit in the commit message.
Also, you don't need to state that you're adding something "to this project"; that is already implied.

#### GOOD Examples

```text
Change data types on myCustomObject

- change field1 from int to bool
- change field2 from long to bool
- change field3 from int to bool

#12345
```

In this example, a multi-line commit message is used.
The first line summarizes what was done, **concisely**.
Additional details are added after the first line (with a blank line after the first line).
Additional lines are simple and concise (just like the first line).
Issue `12345` was referenced for additional information.

```text
Add azureBlobsHelper module
```

This is an example of single-line commit message.
The message simply states that the `azureBlobsHelper` module was added to the project.

## Pull requests

Pull requests serve as the primary mechanism by which contributions are proposed and accepted.

- Open a pull request from your topic branch.
  - For additional guidance, read through the [GitHub Flow Guide][github-flow-guide].
- All commits in the pull request **MUST** be signed.
  - Pull requests with un-signed commits will be rejected.
- Pull request titles **MUST** follow our [commit message](#commit-messages) guidelines.
- Provide a thorough description of the pull request using the template provided.
- Include USEFUL details in the body/description of the pull request.
  - Explain the changes in the pull request in detail as needed.
  - Provide screenshots when appropriate.
    - Show GUI changes.
    - Show API changes in postman.
      - Show successful request/response in detail.
    - Any other screenshots that would help the reviewers of the pull request.
- Don't forget to link to the issue you are solving.
  - e.g. `#123456`
- Be prepared to address feedback on your pull request and iterate if necessary.
  - Following ALL of the points in the [making changes](#making-changes) section will help to reduce iteration cycles.
- We may ask for changes to be made before a pull request can be merged, either using [suggested changes][gh-suggested-changes] or pull request comments.
  - You can apply suggested changes directly through the UI.
    - If you apply the suggested changes via the web UI, but also have local changes to make, ensure that you pull the latest version of your branch before you start to make your changes locally.
  - You can make any other changes in your local branch and push the changes when complete.
- As you update your pull request and commit new changes, reply to each conversation that required your attention.
  - This lets the reviewers know that you have seen and addressed their comments.
- Do NOT resolve conversations created by other users; reviewers will resolve the conversations they start.

<!-- reference urls -->

[gh-md-syntax-guidance]: https://docs.github.com/en/github/writing-on-github/getting-started-with-writing-and-formatting-on-github/basic-writing-and-formatting-syntax
[gh-suggested-changes]: https://docs.github.com/en/github/collaborating-with-issues-and-pull-requests/incorporating-feedback-in-your-pull-request
[github-flow-guide]: https://guides.github.com/introduction/flow/
[repo--appdev-workstation-configuration]: https://dev.azure.com/plantemorancode/AppDev%20Starter%20Kits/_git/appdev-workstation-config
[topic-branch]: https://www.git-scm.com/book/en/v2/Git-Branching-Branching-Workflows#Topic-Branches
