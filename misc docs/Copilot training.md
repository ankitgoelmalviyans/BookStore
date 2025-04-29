
## GitHub Copilot Developer Workshop – Beginner Trainer Guide

### ✅ Day 1 – Detailed Breakdown

#### 🔌 Session 1: Setup & First Flight (90 min)
**Objective:** Help learners understand what GitHub Copilot is, how it works, and how to install and trigger their first suggestion.

**Topics to Teach:**
- **Introduction to GitHub Copilot:**
  - AI-powered coding assistant developed by GitHub and OpenAI.
  - Suggests code in real-time based on context.
- **License Types:**
  - **Free:** Limited features for students/open source contributors.
  - **Pro:** Full feature set for individuals.
  - **Enterprise:** Includes organization-wide policies and admin controls.
- **Installation Steps:**
  - For **VS Code**: From Extensions panel -> Search “GitHub Copilot” -> Install.
  - For **JetBrains IDEs** or **Visual Studio**: Use plugin manager -> search GitHub Copilot -> Install.
- **First Use:**
  - Open a new file (e.g., `.js` or `.py`).
  - Start typing a function comment like `// calculate factorial` or `# generate fibonacci series`.
  - Observe “ghost text” suggestion by Copilot.
  - Press `Tab` to accept.

**Definition of Done:**
- GitHub Copilot is installed and one inline suggestion has been accepted.

**Demo Tips:**
- Preinstall the plugin in different IDEs before the session.
- Show live comparison between how suggestions appear in VS Code vs. JetBrains.
- Explain how to sign in with GitHub account during first install.

---

#### 🧠 Session 2: Code Generation (120 min)
**Objective:** Teach learners to effectively generate code from comments and learn to refine suggestions.

**Topics to Teach:**
- **Triggering Suggestions:**
  - Start with a comment describing the logic (e.g., `// find max element in array`).
  - Use `Tab` to accept full suggestion.
  - Press `Ctrl + ]` to cycle suggestions (or Alt + ] on mac).
- **Granular Acceptance:**
  - Accept just a line or word using cursor control and `Tab`.
- **Writing Better Prompts/Comments:**
  - Be specific (e.g., instead of `sort`, say `sort list in descending order`).
  - Avoid vague or overly short comments.
- **Testing Alternatives:**
  - View Copilot's multiple completions to choose the best fit.

**Definition of Done:**
- The learner generates working code from a comment.
- Learner tries at least two different suggestions.

**Demo Tip:**
- Use beginner-friendly code samples (e.g., factorial, palindrome, string reversal).
- Include one unclear comment on purpose to show limitations (e.g., `// handle user data`).
- Show how different languages like Python and JavaScript respond to comments.

---

#### 📝 Day 1 Quiz (15 min)
**Format:**
- **MCQs**: Test understanding of setup, shortcuts, and suggestion limitations.
- **Task**: Provide a comment like `# check if number is prime`, and ask them to:
  - Generate the code with Copilot.
  - Edit it to handle edge case like 0 or 1.

---

### ✅ Day 2 – Detailed Breakdown

#### 💬 Session 3: Copilot Chat (120 min)
**Objective:** Enable learners to interact with Copilot Chat to understand and refactor code.

**Topics to Teach:**
- **Chat Interfaces:**
  - Panel mode (bottom view), Inline chat (`Ctrl+I`), Quick Chat.
- **Useful Commands:**
  - `/explain`: Break down logic in selected code.
  - `/tests`: Suggest test cases.
  - `/fix`: Suggest fixes for logic or syntax issues.
- **Context Usage:**
  - `#selection`: Acts on selected code only.
  - `@workspace`: Searches and responds in project-wide context.

**Definition of Done:**
- Used Copilot Chat to explain code.
- Successfully executed at least two commands (e.g., `/fix`, `/explain`).

---

#### 🛠 Session 4: Debugging & Refactoring (90 min)
**Objective:** Demonstrate using Copilot for fixing and improving code structure.

**Topics to Teach:**
- Prompt Copilot Chat to find and fix code issues.
- Use `/fix` command on broken functions.
- Improve code clarity and readability using prompts like “make this cleaner” or “refactor to use list comprehension”.

**Definition of Done:**
- Fixed at least one broken function.
- Refactored a block of code using Copilot suggestions.

**Demo Tip:**
- Break a simple function (like missing return or incorrect logic).
- Walk through identifying and fixing it using chat.

---

#### 🧪 Session 5: Test Acceleration (90 min)
**Objective:** Help users auto-generate tests and edge cases using Copilot Chat.

**Topics to Teach:**
- Use `/tests` to generate basic tests.
- Add additional prompts to create edge cases (e.g., null/invalid values).
- Generate mocks: Prompt “create mock data for user object”.

**Definition of Done:**
- Generated valid tests.
- Manually added or edited tests for special cases.

---

#### 📝 Day 2 Quiz (15 min)
**Format:**
- Practical scenario: Given a flawed test suite
  - Fix errors
  - Add edge cases
  - Use `/explain` to describe what tests do

---

### ✅ Day 3 – Detailed Breakdown

#### 📄 Session 6: Docs & Reviews (90 min)
**Objective:** Use Copilot for generating documentation and reviewing PRs.

**Topics to Teach:**
- Generate inline docstrings using `/docs`.
- Summarize code behavior via comments.
- Create commit messages: “write commit message for this change”.
- Add PR descriptions that explain context and change.
- Understand and review AI-generated comments.

**Definition of Done:**
- Generated a docstring and a commit message using Copilot.

---

#### 🔧 Session 7: DevOps with Copilot (90 min)
**Objective:** Introduce Copilot usage for CI/CD and Infrastructure as Code.

**Topics to Teach:**
- Prompt Copilot to write Terraform/Bicep YAML files.
- Create GitHub Actions workflows for build/test/deploy.
- Generate Dockerfiles, K8s manifests.
- Bash/Powershell scripting support.

**Definition of Done:**
- Successfully generated a valid GitHub Actions YAML or Dockerfile.
- YAML passed syntax check in VS Code.

**Demo Tip:**
- Start with empty YAML, then gradually build using prompts like:
  - "CI pipeline for Node app"
  - "Deploy .NET app to Azure"

---

#### 🧠 Session 8: Advanced Use & Ethics (120 min)
**Objective:** Teach advanced prompting, configuration, and responsible use.

**Topics to Teach:**
- **Prompt Engineering:**
  - Be clear and context-rich (e.g., include expected output format).
  - Specify language or constraints.
- **Customization:**
  - Use `.github/copilot-instructions.md` to guide Copilot behavior.
- **Ethics & Safety:**
  - Avoid leaking sensitive data.
  - Don’t blindly copy AI-generated code into prod without review.

**Definition of Done:**
- Wrote 1 custom `.md` instruction.
- Refined 1 weak or generic prompt.

---

#### 📝 Final Quiz (20 min)
**Format:**
- Write 2 real-world prompts:
  - E.g., “Write a login controller in Spring Boot”
  - E.g., “Write tests for file upload feature”
- Given a Copilot suggestion:
  - Choose: Accept / Edit / Reject + explain why

---

#### 🎯 Session 9: Wrap-Up & Feedback (30 min)
**Objective:** Recap all content, address queries, and collect feedback.

**Topics to Cover:**
- Recap all 3 days (Setup, Chat, DevOps)
- Final Q&A
- Collect survey/feedback via Google Form or MS Form
- Share links to:
  - GitHub Copilot documentation
  - Free coding practice resources
  - CI/CD templates for future use

---

### 📌 Reminder for QA Participants:
- Focus more on **test generation, mocking, docstrings, and debugging with chat**.
- In DevOps session, **observe demos**, but trying YAML/Docker creation is optional.
- Day 3's ethics and customization session will help you understand safe usage in test scripts and automation.
- You can later build on CI/CD slowly with examples.
