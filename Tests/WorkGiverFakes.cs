// Fake WorkGivers carrying VANILLA 1.6 class names so AffordanceScanner.PersonWorkPhrase's GetType().Name matching can be
// exercised without the real assembly. Every name below is copied verbatim from the 1.6 Core+Biotech WorkGiverDefs XML or a
// decompiled giver (§23.8 recon, TEST-RESULTS.md). The stub base (Verse.AI.WorkGiver_Scanner) supplies scriptable
// PotentialWorkThingsGlobal/HasJobOnThing/JobOnThing via TestRig, so these fakes only need to exist under the right name.
namespace AIColonistsTests.Fakes
{
    // Warden (Core) — prisoners
    public class WorkGiver_Warden_Chat : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_Warden_Feed : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_Warden_DeliverFood : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_Warden_ReleasePrisoner : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_Warden_TakeToBed : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_Warden_DoExecution : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_ExecuteGuiltyColonist : Verse.AI.WorkGiver_Scanner { } // BasicWorker

    // Doctor (Core) — patients / self
    public class WorkGiver_TendSelf : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_TendSelfEmergency : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_TendOther_Humanlike : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_TendOtherUrgent : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_FeedPatient : Verse.AI.WorkGiver_Scanner { } // DoctorFeedHumanlikes AND DoctorFeedAnimals use this class
    public class WorkGiver_RescueDowned : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_TakeToBedToOperate : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_VisitSickPawn : Verse.AI.WorkGiver_Scanner { }

    // Handling / misc (Core) — animals + prisoners
    public class WorkGiver_Milk : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_Strip : Verse.AI.WorkGiver_Scanner { }

    // Medical bill (Core) — targets the patient pawn when billGiversAllHumanlikes is set
    public class WorkGiver_DoBill : Verse.AI.WorkGiver_Scanner { }

    // Childcare (Biotech) — babies and children in the player faction
    public class WorkGiver_Teach : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_Breastfeed : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_BreastfeedCarryToMom : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_PlayWithBaby : Verse.AI.WorkGiver_Scanner { }
    public class WorkGiver_BottleFeedBaby : Verse.AI.WorkGiver_Scanner { }

    // Hemogen (Biotech) — prisoners
    public class WorkGiver_Warden_DeliverHemogen : Verse.AI.WorkGiver_Scanner { }
}
