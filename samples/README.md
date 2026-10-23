# The example host: the whole base, in one process

What an enterprise application assembled on the base of the Stellaegis family looks like — the `Program.cs` of a
company's own information system: one compartment, the company `acme`, and the shared space of the platform. Its
people have accounts and sign in on the pages (`Stellaegis.Identity`); its issuer turns them into tokens for a native
client (`Stellaegis.Issuer`); its rights are decided per operation (`Stellaegis.Access`); and what gives those rights
their meaning — the catalogue of operations, the roles, the owner, the memberships — is the registry, this
repository. The host has a module of its own, the inventory, and composes the four. One project,
`Stellaegis.Registry.Sample`, port 7131.

The three other packages come from NuGet; the registry references none of them: the host wires the four together,
in three places this example shows — the rights sink on the projection of Access (`AccessRightsSink`, five lines),
the bus that records a registered account in the registry and applies a block (`SampleEventPublisher`), and the
actor read from the token for the compartmented routes (`Callers.PersonOrClient`).

**Declare, install, grant, project, decide** — the whole chain, played below.

## Installing

A PostgreSQL server reachable at `127.0.0.1` as `postgres` without a password — change `Sample:Install:ConnectionString`
otherwise. The installation is a verb of the host, apart from serving, under another role:

```powershell
cd samples/Stellaegis.Registry.Sample
dotnet run -- install --admin-email admin@acme.test --display-name 'Jane Admin'
```

In order, every write under the actor `job:install`:

1. the database `stellaegis_registry_sample` and the role `registry_sample_app`, if missing;
2. the four schemas — `ident`, `oidc`, `authz`, `reg` — installed by the life cycle of Primitives under the
   administration connection: one database, four schemas, one role (the example shares the instance; a deployment
   splits them by configuration alone);
3. **the catalogue**: every operation every module declares — 32 operations from 5 modules: the inventory, the
   accounts, the issuer, Access, the registry — recorded from `GetStellaegisModules()`, nothing seeded by hand; and
   **the roles the application declares** in code (`SampleRoles`): the operations of `owner`, the role the registry
   reserves for the owner of a compartment; `tenant_administrator`, `shop_manager`, `operator` in the compartments;
   `super_admin` on the platform with every operation of the catalogue;
4. **the first administrator**: created in Identity without a password — whose identifier comes back —, and recorded
   in the registry;
5. **the compartment `acme`, created with the administrator seated as its owner**, in one gesture: its first member,
   holding `owner`, which nobody else touches;
6. the administrator made `super_admin` on the platform — at once: the installation is the one actor whose platform
   roles wait for no validation. Each grant goes through the sink into Access;
7. the native client `desktop` and the credentials of the `inventory` module, in the issuer;
8. **the administrator's activation link**, printed: no password goes through the command line, its history or the
   list of processes.

Run again, it changes nothing: the scripts are recorded, the declared roles re-applied, the administrator, the
compartment and its owner exist; an administrator still waiting for their activation gets a new link.

Seating the owner with the compartment is the gesture of a company installing its own system, which knows who holds
it. A host of several compartments installs its operators only: each compartment is created later, without an
owner, and its owner is seated by a designation — an invitation that designates the owner.

## Running

```powershell
cd samples/Stellaegis.Registry.Sample; dotnet run
```

The host verifies the four schemas under the application's role before serving — a schema missing stops it — and
serves `https://localhost:7131`: `/login`, `/activate`, `/account`, `/account/sessions`,
`/.well-known/openid-configuration`, the inventory under `/api/items`, the administration of the four modules under
`/api/admin`. Open the activation link the installation printed, and choose the administrator's password there. The
HTTPS development certificate of the machine must be trusted (`dotnet dev-certs https --trust`), or `curl` takes
`-k`. The example runs in the `Development` environment: the issuer signs with development certificates, which it
refuses anywhere else. Its mailer writes to the log: a link or a code the pages send is read there.

**Every operator signs in with a second factor.** Each operation of the shared space requires one: a session opened
with a password alone reads and writes the inventory of `acme` — the operations of its owner require none —, and is
refused every route of `/api/admin` with `Access.SecondFactorRequired`. Once activated, the administrator enrolls an
authenticator on `/account/mfa/setup` — the QR code, or the key under it —, keeps the recovery codes, and signs in
again: the code is asked on `/login/mfa`, and the tokens carry `amr` `pwd`, `otp`, `mfa`.

## Validating

Some acts wait for a second channel: assigning a platform role, creating a role or changing what it grants, rescuing
a compartment. Asked by a person on the routes, they answer 202 and take effect nowhere. The second channel of the
example is the console — the same executable, under the administration connection, the actor the system account
that runs it:

```powershell
dotnet run -- validate                    # the acts waiting
dotnet run -- validate <act-id>           # validates one: it takes effect at once
dotnet run -- validate <act-id> --reject  # refuses it
```

Whoever stole a web access asks, and the act waits. Another person holding `validations.write` could validate it
on screen as well — `POST /api/admin/registry/validations/{id}/validate` —, never its author.

## Playing the chain

A token for a person comes from the native flow (RFC 8252): the system browser opens the authorization route, the
person signs in on the pages, the browser comes back to a loopback address with a code, the application exchanges
it with its PKCE verifier. By hand, with PowerShell and `curl`:

```powershell
$base = "https://localhost:7131"
function Get-Token {
  $bytes = [byte[]]::new(32); [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
  $verifier  = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
  $hash      = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($verifier))
  $challenge = [Convert]::ToBase64String($hash).TrimEnd('=').Replace('+','-').Replace('/','_')
  # Sign in in the browser, then read the code in the address it lands on: nothing listens on the loopback here,
  # the page fails to load and the URL holds the code.
  Start-Process "$base/connect/authorize?client_id=desktop&redirect_uri=http://127.0.0.1:7890/callback&response_type=code&scope=openid%20offline_access%20email%20profile%20inventory%20issuer&code_challenge=$challenge&code_challenge_method=S256&state=xyz"
  $code = Read-Host "code"
  (curl.exe -sk "$base/connect/token" -d grant_type=authorization_code -d client_id=desktop -d code=$code -d code_verifier=$verifier -d redirect_uri=http://127.0.0.1:7890/callback | ConvertFrom-Json).access_token
}
$admin = Get-Token    # sign in as admin@acme.test, with the code of the authenticator
```

Then the chain:

```powershell
# The administrator, owner of acme: reads and writes the inventory (200, 201).
curl.exe -sk "$base/api/items" -H "Authorization: Bearer $admin"
curl.exe -sk -X POST "$base/api/items" -H "Authorization: Bearer $admin" -H "Content-Type: application/json" -d '{"name":"bolt"}'

# The compartment and its owner, the catalogue the installation recorded, the roles declared — each row with who
# wrote it and when.
curl.exe -sk "$base/api/admin/registry/compartments/acme" -H "Authorization: Bearer $admin"
curl.exe -sk "$base/api/admin/registry/catalog" -H "Authorization: Bearer $admin"
curl.exe -sk "$base/api/admin/registry/roles" -H "Authorization: Bearer $admin"

# GRANT: a role created by hand, over the catalogue — it waits (202, the act), and is not there yet (404).
curl.exe -sk -X POST "$base/api/admin/registry/roles" -H "Authorization: Bearer $admin" -H "Content-Type: application/json" -d '{"name":"storekeeper","scope":"compartment","category":"business","displayName":"Magasinier","operations":["items.read"]}'
curl.exe -sk "$base/api/admin/registry/roles/storekeeper" -H "Authorization: Bearer $admin" -w "%{http_code}"
# In another terminal: the act listed, then validated — the role exists (200).
#   dotnet run -- validate
#   dotnet run -- validate <act-id>
curl.exe -sk "$base/api/admin/registry/roles/storekeeper" -H "Authorization: Bearer $admin"

# A second account, created by the administration route of Identity, without a password: its activation link goes
# to the mailer — the log of the host. The account is recorded in the registry's directory by the bus, by the
# person themselves (createdBy = their own sub). Open the link, choose Paul's password.
$paul = (curl.exe -sk -X POST "$base/api/admin/identity/users" -H "Authorization: Bearer $admin" -H "Content-Type: application/json" -d '{"email":"paul@acme.test","displayName":"Paul Ouvrier","delivery":"email"}' | ConvertFrom-Json).userUniqueId.Replace('-','')
curl.exe -sk "$base/api/admin/registry/users/$paul" -H "Authorization: Bearer $admin"

# Membership and role, given by the owner (204): PROJECT — each write recomputes Paul's rights and hands them to
# Access, at once.
curl.exe -sk -X PUT "$base/api/admin/registry/memberships/acme/$paul" -H "Authorization: Bearer $admin"
curl.exe -sk -X PUT "$base/api/admin/registry/memberships/acme/$paul/roles/storekeeper" -H "Authorization: Bearer $admin"

# DECIDE: Paul signs in (a second token, as paul@acme.test), reads (200), does not write (403), does not purge (403).
$paulToken = Get-Token
curl.exe -sk "$base/api/items" -H "Authorization: Bearer $paulToken"
curl.exe -sk -X POST "$base/api/items" -H "Authorization: Bearer $paulToken" -H "Content-Type: application/json" -d '{"name":"nut"}' -w "%{http_code}"
curl.exe -sk -X DELETE "$base/api/admin/inventory/compartments/acme" -H "Authorization: Bearer $paulToken" -w "%{http_code}"

# A platform role asked for Paul: whoever holds the platform holds every compartment, so it waits (202) — and the
# console refuses it. Paul holds no platform role; the record of the acts keeps who asked and who refused.
curl.exe -sk -X PUT "$base/api/admin/registry/operators/$paul/roles/super_admin" -H "Authorization: Bearer $admin"
#   dotnet run -- validate <act-id> --reject
curl.exe -sk "$base/api/admin/registry/operators/$paul" -H "Authorization: Bearer $admin"
curl.exe -sk "$base/api/admin/registry/validations" -H "Authorization: Bearer $admin"

# The role withdrawn: refused at once (403, Access.Denied); the membership withdrawn: nobody there (403, Access.NotMember).
curl.exe -sk -X DELETE "$base/api/admin/registry/memberships/acme/$paul/roles/storekeeper" -H "Authorization: Bearer $admin"
curl.exe -sk "$base/api/items" -H "Authorization: Bearer $paulToken" -w "%{http_code}"
curl.exe -sk -X DELETE "$base/api/admin/registry/memberships/acme/$paul" -H "Authorization: Bearer $admin"
curl.exe -sk "$base/api/items" -H "Authorization: Bearer $paulToken" -w "%{http_code}"

# The administrator purges the compartment from the platform (200) — an operator of the platform who is also a
# member of a compartment is, in that compartment, in the situation of any member: they read it because they are.
curl.exe -sk -X DELETE "$base/api/admin/inventory/compartments/acme" -H "Authorization: Bearer $admin"
curl.exe -sk "$base/api/items" -H "Authorization: Bearer $admin"
```

Blocking Paul's account (`POST /api/admin/identity/users/{id}/block`) is the other way a right ends: the bus
deactivates him in the registry — which withdraws everything from Access through the sink —, refuses him at the
door at once, and revokes every token the issuer gave him.

## What the example is not

A deployment. One database and one role for four schemas, a secret re-applied from the configuration
(`Stellaegis:Issuer:Clients:inventory:Secret`, so that a service's configuration can name the same — in a deployment
the installation prints it once and it goes to the one service that uses it), development certificates, keys of
the deployment written in `appsettings.json`, a mailer that writes to the log, no scheduler for the purges of
Identity and of the invitations. Everything else is what a host does.
