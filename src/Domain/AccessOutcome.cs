namespace AiDocumentIntelligence.Domain;

// Result of asking "may this caller act on this?". NotFound deliberately covers "exists but you can't see it"
// so a private claim's existence is not revealed to someone who may not view it.
public enum AccessOutcome
{
    Allowed,
    NotFound,
    Forbidden
}
