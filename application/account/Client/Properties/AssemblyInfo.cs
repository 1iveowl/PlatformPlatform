using System.Runtime.CompilerServices;

// The typed client of the account API compiles only against Account.Contracts, never against the server assemblies.
// Its internals are visible to the account tests.
[assembly: InternalsVisibleTo("Account.Tests")]
