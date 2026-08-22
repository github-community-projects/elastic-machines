package slices

func AsPointerSlice[T any](slice []T) []*T {
	pointerSlice := make([]*T, len(slice))

	for i := range slice {
		pointerSlice[i] = &slice[i]
	}

	return pointerSlice
}
