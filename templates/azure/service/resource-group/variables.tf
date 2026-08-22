variable "name" {
  description = "The name of the resource group."
  type        = string
  nullable    = false
}

variable "location" {
  description = "If set, the Azure region the resource group will be created in."
  type        = string
  default     = null
}

variable "tags" {
  description = "A map of tags to apply to the resource group."
  type        = map(string)
  default     = null
}
