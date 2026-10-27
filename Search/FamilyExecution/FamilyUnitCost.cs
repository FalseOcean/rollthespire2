namespace RolltheSpire2.Search.FamilyExecution;

// One complete Family invocation, including an existing whole-table wrapper.
// It cannot describe an order of independently registered Families or promote
// a transport allocation. Missing measurement evidence remains a null quote.
internal sealed record FamilyUnitCost(double Milliseconds, double? SetupMilliseconds, string Evidence);
