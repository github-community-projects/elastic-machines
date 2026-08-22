package shared

import "fmt"

type Environment struct {
	Values  map[string]string
	Omitted []string
}

func NewEnvironment() *Environment {
	return &Environment{
		Values:  make(map[string]string),
		Omitted: []string{},
	}
}

func EnvironmentFromMap(values map[string]string) *Environment {
	if values == nil {
		return NewEnvironment()
	} else {
		return &Environment{
			Values:  values,
			Omitted: []string{},
		}
	}
}

func (env *Environment) ShouldHaveValue(key string, value any) {
	env.Values[key] = fmt.Sprintf("%v", value)
}

func (env *Environment) ShouldNotHave(envVars ...string) {
	env.Omitted = append(env.Omitted, envVars...)
}
