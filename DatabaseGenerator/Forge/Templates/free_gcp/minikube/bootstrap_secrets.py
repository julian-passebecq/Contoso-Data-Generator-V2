"""Explicit local cluster setup. Generates credentials in memory; writes only a Kubernetes Secret."""
import argparse
import json
import secrets
import subprocess


KUBECTL = "kubectl"
CONTEXT = None


def kubectl(*args, input_text=None, check=True):
    context = ["--context", CONTEXT] if CONTEXT else []
    return subprocess.run([KUBECTL, *context, *args], input=input_text, text=True, capture_output=True, check=check)


def main():
    global KUBECTL, CONTEXT
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kubectl", default="kubectl")
    parser.add_argument("--context", help="Explicit local cluster context; never changes the user's current context")
    args = parser.parse_args()
    KUBECTL, CONTEXT = args.kubectl, args.context
    namespace = "contoso-forge"
    # Fail on unreachable clusters; an absent secret is different from a connection error.
    kubectl("cluster-info")
    ns = json.loads(kubectl("get", "namespace", namespace, "--ignore-not-found", "-o", "json").stdout or "null")
    if ns is None:
        kubectl("create", "namespace", namespace)
    existing = kubectl("-n", namespace, "get", "secret", "contoso-forge-metadata", "--ignore-not-found", "-o", "name")
    if existing.stdout.strip():
        print("Existing metadata secret retained; no password rotation performed.")
        return
    password = secrets.token_urlsafe(32)
    document = {
        "apiVersion": "v1", "kind": "Secret",
        "metadata": {"name": "contoso-forge-metadata", "namespace": namespace},
        "type": "Opaque",
        "stringData": {
            "postgres-password": password,
            "connection": f"postgresql+psycopg2://airflow:{password}@contoso-forge-postgres:5432/airflow",
            "simple_auth_manager_passwords.json": json.dumps({"admin": secrets.token_urlsafe(24)}),
        },
    }
    # Secret contents go over stdin, never process arguments, output, or files.
    kubectl("create", "-f", "-", input_text=json.dumps(document))
    print("Created local metadata/auth secret. Read the admin login from Kubernetes when needed.")


if __name__ == "__main__":
    main()
