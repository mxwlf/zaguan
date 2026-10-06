var builder = DistributedApplication.CreateBuilder(args);

// Both admin parameters resolve from configuration first — key `Parameters:<name>`, which in
// practice means this AppHost's user secrets — and fall back to the defaults below. The defaults
// are what make a fresh clone run with no manual setup: AddParameter without one throws
// MissingParameterValueException on the first run, naming a configuration key that nobody has
// written yet.
//
// The username is a plain default rather than a persisted value. There is nothing to generate, so
// writing the constant `admin` to disk would not make it any more stable than the literal below.
// Reading configuration first is what still lets a developer override it per machine by setting
// `Parameters:keyCloakAdminUsername` in user secrets.
var keyCloakAdminUsername = builder.AddParameter(
    "keyCloakAdminUsername",
    builder.Configuration["Parameters:keyCloakAdminUsername"] ?? "admin");

// The password IS persisted, and that is the whole point of `persist: true`: it wraps the
// generator in a UserSecretsParameterDefault, so the value produced on the first run is written to
// this AppHost's user secrets and every later run reads it back out of configuration instead of
// generating a new one. Without it, GenerateParameterDefault mints a fresh password on every run —
// harmless for the container, which is rebuilt anyway, but it means the admin password you looked
// up last time no longer logs you in.
//
// Persistence only happens in run mode, so `aspire publish` and `aspire deploy` are unaffected and
// no generated secret leaks into a manifest.
//
// GenerateParameterDefault's own defaults are deliberately left alone: 22 characters drawn from 67
// possible characters, which is the length Aspire picked to clear 128 bits of entropy.
var keyCloakAdminPassword = builder.AddParameter(
    "keyCloakAdminPassword",
    new GenerateParameterDefault(),
    secret: true,
    persist: true);

// The Keycloak image version is deliberately NOT pinned here: it comes from whichever
// Aspire.Hosting.Keycloak release is restored, which pins its own tag as a const (26.6 in 13.6.0).
// Bumping the package is therefore what moves the Keycloak version, and the package is already
// pinned by Directory.Packages.props plus the committed lock files.
//
// Keycloak starts over on every run, with the realms under Realms/ imported into an empty
// database. That is a deliberate decision rather than an accident of the defaults, so both halves
// of it are stated here:
//
//   * Realms/ is resolved against THIS project's directory, and its contents are copied to
//     /opt/keycloak/data/import — which AddKeycloak already starts the container with
//     `--import-realm` against, so no WithArgs call is needed.
//   * ContainerLifetime.Session ties the container to this AppHost run: it is created on start
//     and deleted on exit, so the next run gets a new container with an empty H2 database.
//     Combined with the absence of a data volume, that is what makes the import run every time,
//     and what makes an edit to a realm file take effect on the next run. Session is already the
//     default, so this call changes no behaviour — it pins it, which is the point. The
//     WithSessionLifetime() shorthand says the same thing but is gated behind the experimental
//     ASPIREPERSISTENCE001 diagnostic, and a suppression is a steep price for a shorter spelling.
//
// This matters because `start --import-realm` imports a realm only when one of that name does not
// already exist — it never overwrites. So WithDataVolume, WithDataBindMount or
// ContainerLifetime.Persistent would each keep Keycloak's database alive across runs, and from
// then on every import is silently skipped and realm edits do nothing until the volume or
// container is deleted by hand. Keep the state throwaway and that failure mode cannot happen.
//
// Note that realm import is a development-time file injection: it is NOT supported by
// `aspire publish` or `aspire deploy`. Seeding a deployed Keycloak means baking the realm into an
// image (WithDockerfile) or driving the admin API from an init job instead.
// See https://aspire.dev/integrations/security/keycloak/.
// Keycloak's own default HTTP port inside the container. Named rather than inlined so the number
// is stated once and S109 has something to point at.
const int keycloakPort = 8080;

builder.AddKeycloak("keycloak", keycloakPort, keyCloakAdminUsername, keyCloakAdminPassword)
    .WithRealmImport("./Realms")
    .WithLifetime(ContainerLifetime.Session);

// MA0032 asks for the CancellationToken overload. There is no token to pass here: this await IS
// the process lifetime, and the host installs its own SIGTERM and Ctrl-C handling to end it.
// Passing CancellationToken.None would satisfy the rule while saying nothing.
#pragma warning disable MA0032
await builder.Build().RunAsync().ConfigureAwait(false);
#pragma warning restore MA0032
