// The browser tests run one class at a time: each class starts its own app and a real browser,
// and running them all at once overloaded developer machines (timeouts in several tests, 2026-10-15/17).
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
