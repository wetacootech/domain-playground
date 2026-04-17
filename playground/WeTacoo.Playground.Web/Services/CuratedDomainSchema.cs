namespace WeTacoo.Playground.Web.Services;

/// Schema curato del dominio WeTacoo per la pagina /bc/schema.
/// Rispecchia il diagramma TO BE DDD 7 (Value Objects in giallo, Entity interne in arancione,
/// Aggregate Root in rosso) indipendentemente dalla semplificazione del codice C#.
/// Obiettivo: mostrare all'utente la rappresentazione completa del dominio target, senza
/// dover promuovere ogni primitivo del codice a VO.
public static class CuratedDomainSchema
{
    private static List<SchemaType>? _cache;

    public static List<SchemaType> GetAll() => _cache ??= Build();

    public static SchemaType? FindEntity(string name)
        => GetAll().FirstOrDefault(s => !s.IsAR && s.Name == name);

    public static SchemaType? FindAR(string name)
        => GetAll().FirstOrDefault(s => s.IsAR && s.Name == name);

    private static List<SchemaType> Build()
    {
        var b = new Builder();

        // =========================================================
        //  COMMERCIAL
        // =========================================================
        b.AR("Salesman", "Commercial")
            .VO("AnagraficaBase").VO("DatiHubspot")
            .Xref("IdentityId", "User");

        b.AR("ProductTemplate", "Commercial")
            .VO("Prodotto").VO("Prezzo").VO("Tipo").VO("OpsData").VO("Condizioni")
            .Xref("AreaId", "Area");

        b.AR("QuestionTemplate", "Commercial")
            .VO("Note").VO("OrigineEReferente").VO("Stato")
            .InnerList("Domande", "QuestionTemplateItem");

        b.Entity("QuestionTemplateItem", "Commercial")
            .VO("Domanda").VO("Condizioni");

        b.AR("Questionnaire", "Commercial")
            .VO("Note").VO("OrigineEReferente").VO("Stato")
            .InnerList("Domande", "Question");

        b.Entity("Question", "Commercial")
            .Xref("QuestionTemplateId", "QuestionTemplate")
            .VO("Domanda").VO("Stato");

        b.AR("Coupons", "Commercial")
            .VO("ScontoCoupon").VO("Condizioni");

        b.AR("Lead", "Commercial")
            .VO("KeyHubspotLead").VO("KeyHubspotCompany")
            .VO("Personal").VO("DatiCommerciali").VO("Coupon").VO("Documenti")
            .VO("State").VO("CustomerData")
            .Xref("IdentityId", "User").Xref("FinancialId", "FinancialClient")
            .Xref("CustomerId", "Lead")
            .XrefList("DealIds", "Deal");

        // Deal.AreaCoperturaId rimosso (review 2026-04-16): l'area non è più sul Deal.
        // Vive dove ha significato operativo: ActivePlan, DraftPlan, ServiceAddress.
        b.AR("Deal", "Commercial")
            .Xref("LeadId", "Lead").Xref("CustomerId", "Lead")
            .Xref("SalesManId", "Salesman").Xref("PreSalesManId", "Salesman")
            .VO("Stato")
            .VO("Documenti").VO("DatiCommerciali").VO("StoricoStati")
            .VO("Permission").VO("Hubspot")
            .Inner("ActivePlan", "ActivePlan")
            .InnerList("ListaQuotation", "Quotation")
            .Inner("PaymentSummary", "PaymentSummary");

        b.Entity("ActivePlan", "Commercial")
            .Xref("QuotationId", "Quotation")
            .VO("DatiPiano").VO("DatiCommerciali").VO("AreaCoperturaId")
            .VO("StoricoOfferte");

        b.Entity("PaymentSummary", "Commercial")
            .VO("DatiEsecuzionePagamento");

        b.Entity("Quotation", "Commercial")
            .Xref("DealId", "Deal")
            .VO("DealFirst").VO("DatiCommerciali")
            .VO("DatiIndirizzo").VO("DatiIndirizzoDestinazione")
            .Inner("Plan", "DraftPlan")
            .InnerList("ListaProdotti", "Product")
            .InnerList("ListaServizi", "ServiceBooked")
            .VO("ListaOggettiStimati")
            .XrefList("ListaOggettiSelezionatiIds", "PhysicalObject")
            .VO("Coupon").VO("QuotationToken").VO("Documenti").VO("PaymentCondition")
            .VOList("ListaQuotes", "QuoteHubspot")
            .InnerList("ListaRiassuntoPagamenti", "PaymentSummary");

        b.Entity("DraftPlan", "Commercial")
            .VO("DatiPiano").VO("AreaCoperturaId").VO("DescrizioneCommerciale");

        b.Entity("Product", "Commercial")
            .Xref("ProductTemplateId", "ProductTemplate")
            .VO("Prezzo").VO("DescrizioneProdotto").VO("NomeProdotto");

        b.Entity("ServiceBooked", "Commercial")
            .VO("Stato").VO("Storico")
            .Xref("QuestionnaireId", "Questionnaire")
            .Xref("WorkOrderId", "WorkOrder")
            .VO("DatiServizioRichiesto").VO("DatiServizioCompletato")
            .VO("ContactData").VO("ServiceAddress")
            .VO("Note")
            .XrefList("MovingIds", "ServiceBooked")
            .VO("DatiOggettiStimati")
            .XrefList("ListaOggettiSelezionatiIds", "PhysicalObject");

        // =========================================================
        //  OPERATIONAL
        // =========================================================
        b.AR("WorkOrder", "Operational")
            .VO("Tipo").VO("OperationalServiceState").VO("MongoDiscriminator")
            .VO("NoteEsecuzione").VO("Note")
            .XrefList("ListaOggettiRichiestiIds", "PhysicalObject")
            .VO("ServiceAddress").VO("DatiIndirizzo").VO("DatiIndirizzoDestinazione")
            .VO("ServiceTimes").VO("ServiceType").VO("DataEsecuzione")
            .VO("CommercialData").VO("OperationalData").VO("ReferralBoxData");

        b.AR("Slot", "Operational")
            .VO("SlotData");

        b.AR("Warehouse", "Operational")
            .VO("DatiMagazzino").VO("Posizione").VO("Stato")
            .VO("AreaCoperturaId").VO("CostiPartner");

        b.AR("Vehicle", "Operational")
            .VO("DatiVeicolo").VO("Stato").VO("AreaCoperturaId").VO("CostiPartner")
            .InnerList("Damages", "VehicleDamage")
            .InnerList("Checks", "VehicleCheck");

        b.Entity("VehicleDamage", "Operational")
            .VO("DatiDanno").VO("Foto");

        b.Entity("VehicleCheck", "Operational")
            .VO("DatiCheck").VO("Esito");

        b.AR("Asset", "Operational")
            .VO("Type").VO("DatiAsset").VO("Stato").VO("AreaCoperturaId").VO("CostiPartner");

        b.AR("Operator", "Operational")
            .Xref("IdentityId", "User")
            .VO("DatiOperatore").VO("Stato").VO("Permission");

        b.AR("Planning", "Operational")
            .VO("DatiPianificazione").VO("AreaCoperturaId")
            .InnerList("Resources", "Resource")
            .InnerList("Teams", "PlanningTeam")
            .InnerList("Missions", "Mission")
            .VO("SupportOperators");

        b.Entity("Resource", "Operational")
            .VO("TypeResource").VO("AreaCoperturaId")
            .VO("OperatorData").VO("VehicleData").VO("AssetData")
            .VO("FasciaOrariaDisponibilita").VO("Note");

        b.Entity("PlanningTeam", "Operational")
            .InnerList("PlannedOperators", "PlannedOperator")
            .VO("Note");

        b.Entity("PlannedOperator", "Operational")
            .Xref("ResourceId", "Resource")
            .VO("OperatorRoles");

        b.Entity("Mission", "Operational")
            .Xref("TeamId", "PlanningTeam")
            .InnerList("ServiceRefs", "ServiceRef")
            .XrefList("ListaVehiclesResourceIds", "Resource")
            .XrefList("ListaAssetsResourceIds", "Resource")
            .VO("FasciaOrariaEsecuzione").VO("Note").VO("Stato");

        b.Entity("ServiceRef", "Operational")
            .Xref("ServiceId", "WorkOrder")
            .VO("VolumePercentage").VO("VolumeOverride");

        // =========================================================
        //  EXECUTION
        // =========================================================
        b.AR("Shift", "Execution")
            .Xref("MissionId", "Mission")
            .VO("TipoIntervento").VO("Autonomo").VO("Data").VO("Stato")
            .InnerList("ListaAttivita", "Task")
            .InnerList("ListaServiziClienti", "ServiceEntry")
            .VO("DatiMissione").VO("RisorseIntervento").VO("Problemi")
            .VO("ListaOggettiStimati")
            .XrefList("ListaOggettiIds", "PhysicalObject");

        b.Entity("Task", "Execution")
            .VO("TipoAttivita").VO("OraInizio").VO("OraFine").VO("Extra")
            .XrefList("ListaOggettiIds", "PhysicalObject")
            .VO("Note").VO("SnapshotDatiOps")
            .Xref("ServiceEntryId", "ServiceEntry");

        b.Entity("ServiceEntry", "Execution")
            .Xref("ServiceId", "WorkOrder")
            .Xref("DealId", "Deal").Xref("LeadId", "Lead")
            .VO("Completed").VO("CompletedAt").VO("Type")
            .VO("ClientData").VO("InspectionData");

        b.AR("VehicleOperation", "Execution")
            .Xref("VehicleId", "Vehicle")
            .VO("Type").VO("TempiEsecuzione").VO("Risorse");

        b.AR("WarehouseOperation", "Execution")
            .Xref("WarehouseId", "Warehouse")
            .Xref("MissionId", "Mission")
            .Xref("VehicleId", "Vehicle")
            .VO("Type").VO("TempiEsecuzione").VO("Risorse").VO("DatiOperazione")
            .XrefList("ListaOggettiIds", "PhysicalObject");

        b.AR("PhysicalObject", "Execution")
            .Xref("LabelId", "Label").Xref("GroupId", "ObjectGroup")
            .Xref("PalletId", "Pallet").Xref("LeadId", "Lead").Xref("DealId", "Deal")
            .VO("Posizione").VO("DatiOggetto").VO("Stato")
            .VO("StoricoObjectSnapshot").VO("Documenti");

        b.AR("Pallet", "Execution")
            .Xref("LabelId", "Label").Xref("LeadId", "Lead").Xref("DealId", "Deal")
            .VO("Posizione").VO("DatiOggetto").VO("Stato")
            .VO("StoricoObjectSnapshot").VO("Documenti")
            .XrefList("ObjectIds", "PhysicalObject");

        b.AR("ObjectGroup", "Execution")
            .VO("DatiGruppo").VO("Foto");

        b.AR("Label", "Execution")
            .VO("DatiLabel").VO("Stato").VO("DataCreazione").VO("DataStampa").VO("Creatore");

        b.AR("LabelReferral", "Execution")
            .VO("DatiLabel").VO("LabelBoxData").VO("Stato").VO("DataCreazione")
            .VO("DataStampa").VO("Creatore");

        // =========================================================
        //  FINANCIAL
        // =========================================================
        b.AR("FinancialClient", "Financial")
            .Xref("CommercialLeadId", "Lead").VO("StripeId")
            .VO("StatoPagamenti").VO("Note").VO("ClientCredit")
            .VO("DatiPosticipo").VO("DatiFatturazione").VO("StoricoDatiFatturazione");

        b.AR("CommercialLeadProjection", "Financial")
            .VO("Anagrafica").VO("Documenti").VO("Contatti")
            .Xref("LeadId", "Lead").VO("DatiFatturazione");

        b.AR("Payment", "Financial")
            .Xref("ClientId", "FinancialClient")
            .Xref("DealId", "Deal").Xref("QuotationId", "Quotation")
            .VO("Stato").VO("Tipo").VO("Iva").VO("DatiFattura")
            .InnerList("Addebiti", "Charge")
            .InnerList("ListaSimplifiedProducts", "SimplifiedProduct");

        b.Entity("Charge", "Financial")
            .VO("Addebiti").VO("DatiStripe").VO("DatiFatture")
            .VO("DatiRimborso").VO("DatiPosticipo").VO("Storico");

        b.Entity("SimplifiedProduct", "Financial")
            .VO("Nome").VO("Descrizione").VO("Prezzo").VO("DataScadenza");

        // =========================================================
        //  IDENTITY
        // =========================================================
        b.AR("User", "Identity")
            .VO("Ruolo").VO("Claims").VO("Email").VO("Username").VO("DatiLogin");

        // =========================================================
        //  MARKETING
        // =========================================================
        b.AR("MarketingClient", "Marketing")
            .Xref("CommercialLeadId", "Lead")
            .VO("FunnelStep")
            .InnerList("ListaDeal", "MarketingDeal");

        b.Entity("MarketingDeal", "Marketing")
            .Xref("DealId", "Deal").Xref("QuotationId", "Quotation")
            .VO("FunnelQuotationStep");

        // =========================================================
        //  HAPPINESS
        // =========================================================
        b.AR("HappinessClient", "Happiness")
            .Xref("CommercialLeadId", "Lead")
            .VO("Punteggi")
            .InnerList("ListaServizi", "HappinessService");

        b.Entity("HappinessService", "Happiness")
            .Xref("ServiceId", "WorkOrder")
            .VO("SoddisfazioneCliente");

        // =========================================================
        //  SHARED INFRASTRUCTURE
        // =========================================================
        b.AR("Area", "SharedInfrastructure")
            .VO("DatiCitta").VO("AreeGeoJson").VO("ListaCap").VO("ConfigurazioniGiorni");

        b.AR("ObjectTemplate", "SharedInfrastructure")
            .VO("DatiOggetto").VO("Tipo")
            .InnerList("Stanze", "Room");

        b.Entity("Room", "SharedInfrastructure")
            .VO("DatiStanza");

        b.AR("Email", "SharedInfrastructure")
            .VO("DatiEmail").VO("Tipo").VO("References").VO("Stato");

        b.AR("Notification", "SharedInfrastructure")
            .VO("DatiNotifica").VO("Stato");

        b.AR("Audit", "SharedInfrastructure")
            .VO("DatiAudit");

        b.AR("Analytics", "SharedInfrastructure")
            .VO("DatiAnalytics");

        return b.All;
    }

    private sealed class Builder
    {
        public readonly List<SchemaType> All = new();
        private SchemaType? _current;

        public Builder AR(string name, string bc)
        {
            _current = new SchemaType(name, bc, true, new List<SchemaProperty>(), null);
            All.Add(_current);
            return this;
        }

        public Builder Entity(string name, string bc)
        {
            _current = new SchemaType(name, bc, false, new List<SchemaProperty>(), null);
            All.Add(_current);
            return this;
        }

        public Builder VO(string name, string? typeName = null)
            => AddProp(name, typeName ?? name, SchemaKind.ValueObject, typeName ?? name);

        public Builder VOList(string name, string typeName)
            => AddProp(name, $"List<{typeName}>", SchemaKind.ValueObjectList, typeName);

        public Builder Enum(string name, string enumType)
            => AddProp(name, enumType, SchemaKind.Enum, enumType);

        public Builder Prim(string name, string type)
            => AddProp(name, type, SchemaKind.Primitive, null);

        public Builder Xref(string name, string targetAR)
            => AddProp(name, "string", SchemaKind.CrossARRef, targetAR);

        public Builder XrefList(string name, string targetAR)
            => AddProp(name, "List<string>", SchemaKind.CrossARRefList, targetAR);

        public Builder Inner(string name, string entityName)
            => AddProp(name, entityName, SchemaKind.InnerEntity, entityName);

        public Builder InnerList(string name, string entityName)
            => AddProp(name, $"List<{entityName}>", SchemaKind.InnerEntityList, entityName);

        private Builder AddProp(string name, string typeName, SchemaKind kind, string? related)
        {
            if (_current == null) throw new InvalidOperationException("No current type.");
            _current.Properties.Add(new SchemaProperty(name, typeName, kind, related, false, false));
            return this;
        }
    }
}
