# 🚀 Azure DevOps Pipeline YAML Overview (Beginner to Advanced)

This guide provides a structured and practical walkthrough of Azure DevOps YAML pipelines, enriched with real-world examples used in Kubernetes, Docker, APIM, Function Apps, and Azure resource provisioning.

---

## ✅ YAML Structure (Top-Level)

```yaml
trigger:   # Triggers the pipeline automatically or manually
variables: # Define global variables used throughout
stages:    # Define build, deploy, release phases
```

---

## ✅ 1. `trigger`

Defines when the pipeline runs.

```yaml
trigger: none                     # Manual run only

trigger:
  branches:
    include: [main]               # Auto-trigger on commits to main
```

---

## ✅ 2. `variables`

Set environment-specific or global values.

```yaml
variables:
- group: BookStoreSecrets         # Azure DevOps variable group
- name: imageTag
  value: '$(Build.BuildId)'       # Auto-increments on each run

- name: azureSubscription
  value: 'AzureServiceConnectionName'
```

---

## ✅ 3. `stages` > `jobs` > `steps`

Structure:
```yaml
stages:
- stage: Build
  jobs:
  - job: DockerBuild
    steps:
    - script: echo "Building image..."
```

---

## ✅ Common Keywords and Syntax

### ✅ `stage`

Defines logical grouping:
```yaml
- stage: BuildStage
  displayName: '🔧 Build Docker Image'
```

### ✅ `job`

Represents a unit of execution:
```yaml
- job: BuildJob
  displayName: 'Build ProductService'
  pool:
    vmImage: 'ubuntu-latest'
```

### ✅ `steps`

All actions like scripts or predefined tasks.

```yaml
steps:
- script: echo "Step running"
- task: Docker@2
  inputs:
    command: build
```

---

## ✅ Task Types and Examples

### ✅ `script`

Use for Bash, PowerShell commands:

```yaml
- script: |
    echo "Hello, $(Build.BuildId)"
    docker version
  displayName: 'Print Docker Version'
```

### ✅ `task`

Use built-in DevOps tasks:

```yaml
- task: AzureWebApp@1
  inputs:
    azureSubscription: $(azureSubscription)
    appName: $(webAppName)
    package: '$(System.DefaultWorkingDirectory)/drop/myapp.zip'
```

---

## ✅ Real Examples from BookStore Project

### 🔐 Create Secrets in Kubernetes

```yaml
- script: |
    echo "Creating productservice secrets..."
    kubectl create secret generic productservice-secrets       --from-literal=Kafka__BootstrapServers="$(KafkaBootstrapServers)"       --namespace="$(namespace)" --dry-run=client -o yaml | kubectl apply -f -
  displayName: 'Create Kubernetes Secrets'
```

### 🐳 Docker Build & Push

```yaml
- task: Docker@2
  inputs:
    command: buildAndPush
    containerRegistry: '$(dockerRegistryServiceConnection)'
    repository: 'bookstore/productservice'
    dockerfile: '**/Dockerfile'
    tags: |
      $(imageTag)
  displayName: 'Build & Push ProductService Image'
```

### 📦 Helm or Ingress Apply

```yaml
- script: |
    echo "Applying Ingress YAML..."
    kubectl apply -f k8s/ingress.yaml -n $(namespace)
  displayName: 'Create Ingress Rule'
```

### ⚙ Deploy Azure Function App (Zip)

```yaml
- task: AzureFunctionApp@1
  inputs:
    azureSubscription: $(azureSubscription)
    appType: 'functionAppLinux'
    appName: $(functionAppName)
    package: $(functionAppZipPath)
  displayName: 'Deploy Proxy Azure Function'
```

---

## ✅ Control Execution

### `dependsOn`: Run jobs in order

```yaml
- job: DeployInventory
  dependsOn: DeployProduct
```

### `condition`: Run only if a condition is met

```yaml
- script: echo "Only if previous job succeeded"
  condition: succeeded()
```

---

## ✅ Advanced Tips

| Feature          | Description |
|------------------|-------------|
| `displayName`    | Helps visualize job/step names in DevOps UI |
| `condition`      | Control flow based on results or variables |
| `dependsOn`      | Control execution order of jobs |
| `continueOnError`| Allow pipeline to continue on failure |

---

## ✅ Execution Order (Behind the Scenes)

1. **trigger**  
2. **variables**
3. **stages**
    - **jobs**  
      - **steps**
        - `script`, `task`

Each **job** runs in parallel unless controlled by `dependsOn`.

---

## ✅ Common Mistakes to Avoid

| Mistake | Correction |
|--------|------------|
| Missing indentation | Always use 2 spaces (no tabs) |
| Using `env:` instead of `variables:` | Prefer top-level `variables:` block |
| Secrets hardcoded | Always use secure Azure DevOps variable groups |
| Job order issues | Use `dependsOn` to enforce job sequence |

---

## 🧠 YAML Learning Boost: Try These in Your Pipeline

| Goal | Snippet |
|------|---------|
| ✅ Deploy AKS YAML | `kubectl apply -f yourfile.yaml` |
| ✅ Fetch AKS Context | `az aks get-credentials` |
| ✅ Port-forward Debug | `kubectl port-forward svc/myservice 8080:80 -n bookstore` |
| ✅ Validate Secrets | `kubectl describe secret productservice-secrets -n bookstore` |
| ✅ Use `Build.BuildId` for tag | `- name: imageTag value: '$(Build.BuildId)'` |

---
