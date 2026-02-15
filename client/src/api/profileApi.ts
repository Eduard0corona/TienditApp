import api from './axios';
import type {
  UserProfileDto,
  UpdateProfileRequest,
  ChangePasswordRequest,
  AvatarImageUrlsDto,
  ApiMessage,
} from '../types';

export const profileApi = {
  getProfile: () =>
    api.get<UserProfileDto>('/auth/profile'),

  updateProfile: (data: UpdateProfileRequest) =>
    api.put<UserProfileDto>('/auth/profile', data),

  uploadAvatar: (file: File) => {
    const formData = new FormData();
    formData.append('file', file);
    return api.post<AvatarImageUrlsDto>('/auth/profile/avatar', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
  },

  deleteAvatar: () =>
    api.delete('/auth/profile/avatar'),

  changePassword: (data: ChangePasswordRequest) =>
    api.post<ApiMessage>('/auth/profile/change-password', data),
};
