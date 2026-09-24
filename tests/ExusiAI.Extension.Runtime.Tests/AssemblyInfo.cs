using Xunit;

// WPF owns process-wide dispatcher/theme state. Running independent STA smoke tests in
// parallel can deadlock during resource lookup even though each page is valid in isolation.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
