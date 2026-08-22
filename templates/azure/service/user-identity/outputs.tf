output "is_configured" {
  description = "Whether or not a user-assigned identity is configured."
  value       = local.user_assigned_identity
}

output "details" {
  description = "The details of the user-assigned identity, if configured."
  value       = local.user_identity
}
