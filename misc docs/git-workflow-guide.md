# ✅ Git Workflow (Team-Friendly with Azure Repos)

> For a feature development process using `main` branch and Azure DevOps.

---

## 🔁 1. Clone the Repository
Clone the Azure repo to your local machine:
```bash
git clone <repo-url>
cd <repo-folder>
```

---

## 🔄 2. Pull the Latest Changes from `main`
Make sure you’re synced with the latest version of `main`:
```bash
git checkout main
git pull origin main
```

---

## 🌿 3. Create & Checkout a New Feature Branch
Create a dedicated branch for your feature or bug fix:
```bash
git checkout -b feature/feature-1
```

---

## ✍️ 4. Make Code Changes
Do your development work and then stage the changes:
```bash
git add .
git commit -m "Added feature-1 logic"
```

---

## 🚀 5. Push the Feature Branch to Azure Repo
Push your local feature branch to the remote repository:
```bash
git push origin feature/feature-1
```

---

## 🔃 6. Create a Pull Request (PR)
- Go to **Azure DevOps → Repos → Pull Requests**
- Click on **"New Pull Request"**
- Set:
  - **Source branch**: `feature/feature-1`
  - **Target branch**: `main`
- Add title, description, and assign reviewers
- Submit for review

---

## ✅ 7. Review and Merge
After approval:
- Reviewer merges the feature branch into `main`
- You can also **"Squash" or "Rebase"** if your team prefers

---

## ⬇️ 8. Update Your Local `main`
After PR is merged:
```bash
git checkout main
git pull origin main
```

---

## 🧹 9. (Optional) Delete Feature Branch
Clean up both local and remote branches:
```bash
git branch -d feature/feature-1                 # local delete
git push origin --delete feature/feature-1      # remote delete
```

---

## 💡 Notes:
- Never push directly to `main`
- Always create a new feature branch per task/bug
- Always pull before you start new work to avoid conflicts
- Keep PR titles and descriptions meaningful for reviewers