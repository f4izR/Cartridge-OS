using CartridgeOS.Service;

var builder = Host.CreateApplicationBuilder(args);

// Runs fine as a plain console process during development; this is what makes it behave as a real
// Windows Service once installed via `sc create` — SCM start/stop control and event-log logging.
// The SCM-level restart-on-crash policy itself is configured at install time (`sc failure ...`), not
// here — that's the installer's job (see production-readiness.md), this just makes the service
// controllable enough for that policy to have something to restart.
builder.Services.AddWindowsService(options => options.ServiceName = "CartridgeOS");
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
