using Gloaming.Agents.Workflows.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

Console.WriteLine($"PASS independently packaged {typeof(EfCheckpointStore<DbContext>).Name}");
