var builder = DistributedApplication.CreateBuilder(args);

var keyCloakAdminUsername = builder.AddParameter("keyCloakAdminUsername");
var keyCloakAdminPassword = builder.AddParameter("keyCloakAdminPassword", secret: true);

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
builder.AddKeycloak("keycloak", 8080, keyCloakAdminUsername, keyCloakAdminPassword)
    .WithRealmImport("./Realms")
    .WithLifetime(ContainerLifetime.Session);

await builder.Build().RunAsync().ConfigureAwait(false);
