# Azure DevOps Pipeline YAML Overview

This document gives a light and practical overview of Azure DevOps YAML pipeline structure including the order of elements and purpose of each.

---

## ✅ YAML Structure (Top-Level)

```yaml
trigger:   # Triggers the pipeline
variables: # Define global variables
stages:    # Define one or more stages
```

### 1. `trigger`
Defines when the pipeline runs (on commit, manual, or none).
```yaml
trigger: none            # Manual only
trigger:
  branches:
    include: [main]      # Auto-trigger on commits to main branch
```

### 2. `variables`
Reusable variables you can reference throughout the pipeline.
```yaml
variables:
  azureSubscription: 'MyConnection'
  webAppName: 'my-app-name'
```

---

## ✅ `stages` > `jobs` > `steps`

- `stages`: Logical divisions like Build, Test, Release.
- `jobs`: Runs in parallel unless told to depend on another.
- `steps`: Actual commands or tasks inside a job.

```yaml
stages:
- stage: Build
  jobs:
  - job: CompileCode
    steps:
    - script: echo "Compiling code..."
```

---

## ✅ Common Keywords

### `stage`
```yaml
- stage: BuildStage
  displayName: 'Build Stage'
```

### `job`
```yaml
- job: BuildJob
  displayName: 'Run Build'
  pool:
    vmImage: 'ubuntu-latest'
```

### `steps`
The actual actions within a job.
```yaml
steps:
- script: echo "Hello World"
- task: AzureWebApp@1
  inputs:
    appName: $(webAppName)
```

---

## ✅ Task Types

### `script`
Run shell or PowerShell commands directly.
```yaml
- script: npm install
```

### `task`
Run predefined Azure DevOps tasks.
```yaml
- task: AzureWebApp@1
  inputs:
    azureSubscription: $(azureSubscription)
    appName: $(webAppName)
    package: '$(System.DefaultWorkingDirectory)/drop/myapp.zip'
```

---

## ✅ Execution Order

1. `trigger` (decides if pipeline runs)
2. `variables` (set upfront)
3. `stages`
   - `jobs`
     - `steps`
       - `scripts` / `tasks`

Each job in a stage runs in parallel unless you use `dependsOn` to enforce order.

---

## ✅ Tips

- Use `displayName` to improve readability in the DevOps UI.
- Comment with `#` to explain parts.
- Use `dependsOn` in jobs to enforce order.
```yaml
  - job: DeployUI
    dependsOn: DeployBackend
```
- Use `condition` to control when a step runs.

---

Let me know if you want a deeper example with Build + Test + Release combined in one YAML!

