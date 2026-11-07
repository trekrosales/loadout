// Some tests change the LOADOUT_DATA_DIR environment variable, which is process-wide,
// so tests run one at a time. They are fast, so this costs little.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
