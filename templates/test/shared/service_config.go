package shared

type ServiceConfig struct {
	Name      string            `yaml:"name"`
	Container *ContainerConfig  `yaml:"container"`
	Tags      map[string]string `yaml:"tags"`
}

type ContainerConfig struct {
	Image       string
	Version     string         `yaml:"version"`
	Environment map[string]any `yaml:"environment"`
}
