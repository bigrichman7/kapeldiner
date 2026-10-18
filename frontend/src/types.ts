export type Role = 'Administrator' | 'User';

export interface Session {
  id: string;
  login: string;
  name: string;
  role: Role;
}

export interface UserProfile {
  id: string;
  login: string;
  email: string;
  name: string;
  role: Role;
  createdAtUtc: string;
}

export interface EventQuota {
  availableSeats: number;
  usedSeats: number;
  remainingSeats: number;
  userId?: string;
}

export interface EventItem {
  eventId: string;
  title: string;
  description: string | null;
  startAt: string;
  endAt: string;
  status: 'draft' | 'published' | 'completed' | 'cancelled';
  createdAt: string;
  bookingOpen: boolean;
  myQuota: EventQuota | null;
  quotas: EventQuota[] | null;
  enableAdditionalRow12A: boolean;
}

export interface EventNotificationFailure {
  userId: string;
  name: string | null;
  email: string | null;
  code: string;
  message: string;
}

export interface EventNotificationSummary {
  recipientCount: number;
  sentCount: number;
  failedCount: number;
  failures: EventNotificationFailure[];
}

export interface EventPublishResponse {
  event: EventItem;
  notifications: EventNotificationSummary;
}

export interface UserSeat {
  seatId: string;
  available: boolean;
}

export interface AdminSeat {
  seatId: string;
  designation: 'vip' | 'organization' | null;
  booked: boolean;
  userId: string | null;
  userName: string | null;
}

export interface Reservation {
  reservationId: string | null;
  eventId: string;
  userId: string;
  seatIds: string[];
  availableSeats: number;
  usedSeats: number;
  remainingSeats: number;
}

export interface Quota {
  eventId: string;
  userId: string;
  login: string | null;
  name: string | null;
  availableSeats: number;
  usedSeats: number;
  remainingSeats: number;
}

export interface EventStatistics {
  eventId: string;
  totalSeats: number;
  reservedSeats: number;
  unreservedSeats: number;
  userBookableSeats: number;
  allocatedQuotaSeats: number;
  usedQuotaSeats: number;
  remainingQuotaSeats: number;
  reservationCount: number;
  users: Array<{
    userId: string;
    name: string | null;
    availableSeats: number;
    usedSeats: number;
    remainingSeats: number;
  }>;
}

export interface Envelope<T> {
  success: boolean;
  data: T | null;
  error: {
    code: string;
    message: string;
    fields?: Record<string, string[] | null> | null;
  } | null;
}
