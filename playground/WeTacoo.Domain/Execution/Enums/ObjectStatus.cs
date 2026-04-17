namespace WeTacoo.Domain.Execution.Enums;
public enum ObjectStatus { Draft, PickedUp, OnVehicle, OnWarehouse, Requested, ToManage, Alerted, Delivered, Disposed }
/// <summary>
/// DDD5 §2.1 (review 2026-04-16): tipologie di service entries per uno Shift.
/// I tipi "Trasloco*" indicano che il servizio e' appaiato via ServiceBooked.MovingIds;
/// i tipi "*TraslocoDeposito" indicano ritiri/consegne di trasloco che poi transitano o partono da un deposito.
/// </summary>
public enum ServiceEntryType { Ritiro, TraslocoRitiro, RitiroTraslocoDeposito, TraslocoConsegna, ConsegnaTrasloco, ConsegnaTraslocoDeposito, Consegna, Smaltimento, Sopralluogo }
public enum TaskType { Censimento, Smontaggio, Imballaggio, Facchinaggio, Carico, Scarico, Disimballaggio, Rimontaggio, Ingresso, Uscita, Trasbordo, Pausa, Movimento }
public enum OperationStatus { InProgress, Paused, Suspended, Completed, Interrupted }
public enum OperationCategory { Client, Vehicle, Warehouse }
public enum OperationType { RitiroCliente, ConsegnaCliente, ScaricoVeicolo, CaricoVeicolo, MagazzinoIn, MagazzinoOut, Trasbordo, Smaltimento }
