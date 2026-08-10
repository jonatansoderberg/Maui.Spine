# Orientera.Domain

Domänmodellen och källkontrakten, delade av appen, `Orientera.Backend` och testerna:
`Competition`, `EventGroup`, `CompetitionProfile`, `Person`/`FollowedPerson`,
`Entry`/`Start`/`Result`, `Split`/`LegAnalysis`, `SeriesStanding`, `RankingSnapshot`,
`Prediction`, `Course`/`Control`/`Route`, `ContextState` — och `Sources/` med
`IEventSource`, `IParticipationSource`, `ILiveSource`, `IPeopleSource`, `IProgressSource`.

Biblioteket är fritt från MAUI och från allt som vet var datan kommer ifrån. Domänlogik ska
vara unit-testbar utan UI.
