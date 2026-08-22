################################################################
# Define GHCR and config secrets to be used by our container
################################################################

locals {
  ghcr_credentials = {
    "username" = "${var.container.login}"
    "password" = "${sensitive(var.container.token)}"
  }

  secret_arns = distinct([
    for _, envValue in var.container.environment : join(":", slice(split(":", envValue.secret), 0, 7))
    if can(envValue.secret)
  ])
}

resource "aws_secretsmanager_secret" "elastic_machines_secret_ghcr" {
  name                    = "${var.config_name}-GHCR-creds"
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "GHCR" {
  secret_id     = aws_secretsmanager_secret.elastic_machines_secret_ghcr.id
  secret_string = jsonencode(local.ghcr_credentials)
}
