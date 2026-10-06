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
//
// ---------------------------------------------------------------------------------------------
// EVERY PORT THIS REPOSITORY BINDS LIVES IN 242xx, ON PURPOSE
// ---------------------------------------------------------------------------------------------
//   24210/24211  Aspire dashboard (https/http)    } Properties/launchSettings.json
//   24220/24221  Aspire OTLP endpoint             }
//   24230/24231  Aspire resource service          }
//   24280        Keycloak                         keycloakPort below
//
// One contiguous block rather than the template's defaults, because the defaults are what every
// other stack on a developer's machine reaches for too. This is not hypothetical: the template
// these numbers came from is also the source of the sibling indicia-focus-chat repository, so the
// two asked for the IDENTICAL dashboard, OTLP and resource-service ports AND the same Keycloak
// 8080, and could not run side by side at all. 8080 is additionally taken by an unrelated local
// stack on the machines this runs on. That sibling now owns 241xx; this one owns 242xx, so
// "does this collide?" is one range check instead of seven.
//
// Note that the number below is the HOST side only. Keycloak still serves its own 8080 and 8443
// inside the container's network namespace, untouched — AddKeycloak maps the host port onto the
// container's 8080 for us.
//
// Named rather than inlined so the number is stated once and S109 has something to point at.
const int keycloakPort = 24280;

builder.AddKeycloak("keycloak", keycloakPort, keyCloakAdminUsername, keyCloakAdminPassword)
    .WithRealmImport("./Realms")
    // Raises the request-header limits from Vert.x's 8192-byte default, because a shared localhost
    // cookie jar overflows it and the symptom is a bare 431 on the admin console or a login page.
    // Cookies are scoped by host, not by port or scheme, so every other application a developer
    // runs on localhost contributes to the headers this container receives.
    //
    // Delivered as a mounted properties file because Keycloak offers no option of its own and
    // ignores the equivalent QUARKUS_ environment variable. Note that TWO limits are involved — the
    // HTTP/1.1 one and the separate HTTP/2 one, which is the only one a browser is subject to here.
    // Both the reasoning and the measurements are in the file.
    .WithBindMount("./Keycloak/quarkus.properties", "/opt/keycloak/conf/quarkus.properties", isReadOnly: true)
    .WithLifetime(ContainerLifetime.Session);

// MA0032 asks for the CancellationToken overload. There is no token to pass here: this await IS
// the process lifetime, and the host installs its own SIGTERM and Ctrl-C handling to end it.
// Passing CancellationToken.None would satisfy the rule while saying nothing.
#pragma warning disable MA0032
await builder.Build().RunAsync().ConfigureAwait(false);
#pragma warning restore MA0032
