using LostAndFound.App.Features.Admin;
using LostAndFound.App.Features.Auth;
using LostAndFound.App.Features.Claims;
using LostAndFound.App.Features.Disputes;
using LostAndFound.App.Features.Listings;
using LostAndFound.App.Features.Matching;
using LostAndFound.App.Infrastructure;

namespace LostAndFound.Desktop.Shared;

public sealed record ServiceContext(
    AuthService Auth,
    AdminService Admin,
    ListingService Listings,
    MatchingService Matching,
    ClaimService Claims,
    DisputeService Disputes,
    BackupService Backups,
    LocalImageStore Images,
    AppPaths Paths);
