namespace WeTacoo.Domain.Commercial.Enums;

// Tipi di ServiceBooked (riga vendibile di Quotation). Il Sopralluogo NON e' un ServiceBooked:
// e' un WorkOrder dedicato (WorkOrderType.Sopralluogo) creato su richiesta da un ServiceBooked
// esistente. Vedi DDD5 §2.2 / §4.8 e InspectionId sul ServiceBooked.
public enum ServiceBookedType { Ritiro, Consegna, Smaltimento }
