namespace LibraryLending.Domain;

public enum CopyStatus { Available, Borrowed, Reserved, Lost }
public enum MembershipStatus { Active, Suspended, Expired }
public enum ReservationStatus { Waiting, Ready, Fulfilled, Expired, Cancelled }
