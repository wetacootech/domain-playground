namespace WeTacoo.Domain.Execution.Enums;
public enum ObjectStatus { Draft, PickedUp, OnVehicle, OnWarehouse, Requested, ToManage, Alerted, Delivered, Disposed }
public enum ServiceEntryType { Ritiro, TraslocoRitiro, TraslocoConsegna, Consegna, Smaltimento, Sopralluogo }
public enum TaskType { Censimento, Smontaggio, Imballaggio, Facchinaggio, Carico, Scarico, Disimballaggio, Rimontaggio, Ingresso, Uscita, Trasbordo, Pausa, Movimento }
public enum OperationStatus { InProgress, Paused, Suspended, Completed, Interrupted }
public enum OperationCategory { Client, Vehicle, Warehouse }
public enum OperationType { RitiroCliente, ConsegnaCliente, ScaricoVeicolo, CaricoVeicolo, MagazzinoIn, MagazzinoOut, Trasbordo, Smaltimento }
