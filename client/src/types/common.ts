// Shapes every part of the API shares: a page of a list, an error and a message.

export interface ApiError {
  error: string;
}

export interface ApiMessage {
  message: string;
}

export interface PaginatedList<T> {
  items: T[];
  pageNumber: number;
  totalPages: number;
  totalCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
